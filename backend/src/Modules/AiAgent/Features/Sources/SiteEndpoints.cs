using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Ingestion;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Shipping.Contracts;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.AiAgent.Features.Sources;

/// <summary><see cref="Confirmed"/>: the owner says the site is theirs or that they may use it.</summary>
internal sealed record SiteRequest(string? Domain, bool Confirmed);

internal sealed record SitePagesRequest(IReadOnlyList<string>? Urls);

internal sealed record SitePageResponse(string Url, string Title, bool Chosen, Guid? SourceId, string? SourceStatus);

internal sealed record SiteResponse(
    Guid Id,
    string Host,
    string Status,
    string? FailureReason,
    IReadOnlyList<SitePageResponse> Pages,
    DateTimeOffset CreatedAt);

internal sealed record SitePagesAdded(int Added, int AlreadyAdded);

internal sealed record SiteUsed(int Used, int NotReady);

internal sealed record SiteDifferences(int Count);

internal sealed class SiteValidator : AbstractValidator<SiteRequest>
{
    public SiteValidator()
    {
        RuleFor(r => r.Domain).NotEmpty().MaximumLength(KnowledgeSite.HostMaxLength);
        RuleFor(r => r.Confirmed).Equal(true).WithMessage("Confirm that this site is yours or that you may use it.");
    }
}

internal sealed class SitePagesValidator : AbstractValidator<SitePagesRequest>
{
    public SitePagesValidator() =>
        RuleFor(r => r.Urls).NotEmpty().Must(u => u is null || u.Count <= PageChooser.MaxListed).WithMessage($"Choose at most {PageChooser.MaxListed} pages.");
}

internal static class SiteErrors
{
    /// <summary>Also for other workspaces' sites (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("site.not_found", "Site not found.");

    public static readonly Error Invalid = Error.Validation("site.invalid_domain", "Enter a website address, for example example.com.");

    public static readonly Error Blocked = Error.Validation("site.blocked", "This address isn't a public website.");

    public static readonly Error Unreachable = Error.Validation("site.unreachable", "This website can't be reached.");

    public static readonly Error Duplicate = Error.Conflict("site.duplicate", "This website was already added.");

    public static readonly Error LimitReached = Error.Conflict("site.limit_reached", $"This workspace has reached {KnowledgeSite.MaxPerWorkspace} websites.");

    public static readonly Error NotReady = Error.Conflict("site.not_ready", "The pages of this website haven't been found yet.");

    public static readonly Error UnknownPage = Error.Validation("site.unknown_page", "Choose pages from the list of this website.");
}

/// <summary>
/// <c>/api/v1/ai/sites</c> (Issue #80, ADR 0030): every member sees the sites, Owner and Admin change
/// them. Only addresses the job found can be added, so this API never fetches what a request names.
/// </summary>
internal static class SiteEndpoints
{
    private const int MaxSectionsChecked = 5000;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var sites = endpoints.MapGroup("/api/v1/ai/sites").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        sites.MapGet("/", async (AiDbContext db, CancellationToken ct) =>
        {
            var list = await db.Sites.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
            var sources = await db.Sources.AsNoTracking().Where(s => s.SiteId != null).ToListAsync(ct);
            return Results.Ok(list.Select(s => ToResponse(s, sources)).ToList());
        });

        sites.MapGet("/{id:guid}", async (Guid id, AiDbContext db, CancellationToken ct) =>
        {
            var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
            return site is null
                ? SiteErrors.NotFound.ToProblem()
                : Results.Ok(ToResponse(site, await db.Sources.AsNoTracking().Where(s => s.SiteId == id).ToListAsync(ct)));
        });

        // Before "Use all for AI": from the settings as they are now, like a source's review page.
        sites.MapGet("/{id:guid}/differences", async (Guid id, AiDbContext db, IShippingRates shipping, CancellationToken ct) =>
            {
                if (!await db.Sites.AnyAsync(s => s.Id == id, ct))
                {
                    return SiteErrors.NotFound.ToProblem();
                }

                var sourceIds = await db.Sources.AsNoTracking().Where(s => s.SiteId == id).Select(s => s.Id).ToListAsync(ct);
                var sections = await db.Sections.AsNoTracking().Where(s => sourceIds.Contains(s.SourceId)).Take(MaxSectionsChecked).ToListAsync(ct);
                var returnDays = await db.Policies.AsNoTracking().Select(p => p.ReturnDays).SingleOrDefaultAsync(ct);
                return Results.Ok(new SiteDifferences(ConflictCheck.Find(sections, new BusinessFacts(await shipping.ListAsync(ct), returnDays)).Count));
            })
            .AddEndpointFilter(manage);

        sites.MapPost("/", AddAsync)
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<SiteRequest>>()
            .RequireRateLimiting(RatePolicies.Uploads);

        sites.MapPost("/{id:guid}/pages", AddPagesAsync)
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<SitePagesRequest>>()
            .RequireRateLimiting(RatePolicies.Uploads);

        // "Use all for AI": the pages that were read; the others can follow when they are.
        sites.MapPost("/{id:guid}/use", async (Guid id, AiDbContext db, IAuditTrail audit, CancellationToken ct) =>
            {
                if (!await db.Sites.AnyAsync(s => s.Id == id, ct))
                {
                    return SiteErrors.NotFound.ToProblem();
                }

                var pages = await db.Sources.Where(s => s.SiteId == id).ToListAsync(ct);
                var used = pages.Count(p => p.Use());
                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("knowledge.site_used", ct);
                return Results.Ok(new SiteUsed(used, pages.Count(p => p.Status is not (SourceStatus.Live or SourceStatus.Review))));
            })
            .AddEndpointFilter(manage);

        // Its pages and their search data go with it (cascade, SEC-45).
        sites.MapDelete("/{id:guid}", async (Guid id, AiDbContext db, IAuditTrail audit, CancellationToken ct) =>
            {
                var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == id, ct);
                if (site is null)
                {
                    return SiteErrors.NotFound.ToProblem();
                }

                db.Sites.Remove(site);
                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("knowledge.site_deleted", ct);
                return Results.NoContent();
            })
            .AddEndpointFilter(manage);
    }

    private static async Task<IResult> AddAsync(
        SiteRequest request, AiDbContext db, SafeHttpClient http, ITeamDirectory team, ITenantContext tenant, IAuditTrail audit, CancellationToken ct)
    {
        if (!SiteAddress.TryParse(request.Domain, out var host, out var root))
        {
            return SiteErrors.Invalid.ToProblem();
        }

        try
        {
            await http.CheckAsync(root, ct);
        }
        catch (SafeHttpException ex)
        {
            return (ex.Failure switch
            {
                SafeHttpFailure.BlockedAddress => SiteErrors.Blocked,
                SafeHttpFailure.Unreachable => SiteErrors.Unreachable,
                _ => SiteErrors.Invalid,
            }).ToProblem();
        }

        if (await db.Sites.CountAsync(ct) >= KnowledgeSite.MaxPerWorkspace)
        {
            return SiteErrors.LimitReached.ToProblem();
        }

        if (await db.Sites.AnyAsync(s => s.Host == host, ct))
        {
            return SiteErrors.Duplicate.ToProblem();
        }

        var me = (await team.CurrentAsync(ct))!;
        var site = KnowledgeSite.Create(tenant.TenantId, host, root.AbsoluteUri, me.UserId);
        db.Sites.Add(site);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return SiteErrors.Duplicate.ToProblem();
        }

        await audit.RecordAsync("knowledge.site_added", ct);
        return Results.Ok(ToResponse(site, []));
    }

    private static async Task<IResult> AddPagesAsync(
        Guid id, SitePagesRequest request, AiDbContext db, ITeamDirectory team, ITenantContext tenant, IAuditTrail audit, CancellationToken ct)
    {
        var site = await db.Sites.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (site is null)
        {
            return SiteErrors.NotFound.ToProblem();
        }

        if (site.Status != SiteStatus.Ready)
        {
            return SiteErrors.NotReady.ToProblem();
        }

        var urls = request.Urls!.Distinct(StringComparer.Ordinal).ToList();
        var known = site.Pages.Select(p => p.Url).ToHashSet(StringComparer.Ordinal);
        if (!urls.All(known.Contains))
        {
            return SiteErrors.UnknownPage.ToProblem();
        }

        var existing = await db.Sources.Where(s => s.Kind == SourceKind.WebPage && urls.Contains(s.Origin)).Select(s => s.Origin).ToListAsync(ct);
        var added = urls.Except(existing, StringComparer.Ordinal).ToList();
        if (await db.Sources.CountAsync(ct) + added.Count > KnowledgeSource.MaxPerWorkspace)
        {
            return SourceErrors.LimitReached.ToProblem();
        }

        var me = (await team.CurrentAsync(ct))!;
        db.Sources.AddRange(added.Select(url => KnowledgeSource.FromWebPage(tenant.TenantId, url, me.UserId, id)));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return SourceErrors.Duplicate.ToProblem();
        }

        await audit.RecordAsync("knowledge.site_pages_added", ct);
        return Results.Ok(new SitePagesAdded(added.Count, existing.Count));
    }

    private static SiteResponse ToResponse(KnowledgeSite site, IReadOnlyList<KnowledgeSource> sources) => new(
        site.Id,
        site.Host,
        site.Status.ToString().ToLowerInvariant(),
        site.FailureReason,
        site.Pages.Select(p =>
        {
            var source = sources.FirstOrDefault(s => s.SiteId == site.Id && s.Origin == p.Url);
            return new SitePageResponse(p.Url, p.Title, p.Chosen, source?.Id, source?.Status.ToString().ToLowerInvariant());
        }).ToList(),
        site.CreatedAt);
}
