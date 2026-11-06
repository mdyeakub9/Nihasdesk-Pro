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

namespace Nihasdesk.Modules.AiAgent.Features.Sources;

internal sealed record ConflictResponse(string Kind, Guid SectionId, int? Page, string Found, string Expected);

internal sealed record SourceReviewResponse(SourceResponse Source, IReadOnlyList<ConflictResponse> Conflicts, IReadOnlyList<Guid> PriceListSections);

/// <summary><see cref="Searchable"/>: false when the embedding is missing, so only keywords find it.</summary>
internal sealed record SectionResponse(Guid Id, int Position, string? HeadingPath, int? Page, string Kind, string Text, bool Excluded, bool Searchable);

/// <summary><see cref="NextAfter"/>: the position to ask from next, or null at the end.</summary>
internal sealed record SectionPageResponse(IReadOnlyList<SectionResponse> Items, int? NextAfter);

internal sealed record SectionTextRequest(string? Text);

/// <summary>"Test a question": <see cref="SourceId"/> limits it to one source; otherwise all sources being reviewed or used.</summary>
internal sealed record SearchTestRequest(string? Query, Guid? SourceId);

internal sealed record PassageResponse(Guid SectionId, Guid SourceId, string SourceTitle, string? HeadingPath, int? Page, string Text, double? Similarity);

internal sealed class SearchTestValidator : AbstractValidator<SearchTestRequest>
{
    public SearchTestValidator() =>
        RuleFor(r => r.Query).NotEmpty().Must(q => !string.IsNullOrWhiteSpace(q)).MaximumLength(300);
}

internal sealed class SectionTextValidator : AbstractValidator<SectionTextRequest>
{
    public SectionTextValidator() =>
        RuleFor(r => r.Text).NotEmpty().Must(t => !string.IsNullOrWhiteSpace(t)).MaximumLength(KnowledgeSection.TextMaxLength);
}

internal static class ReviewErrors
{
    public static readonly Error NoNextSection = Error.Conflict("source.no_next_section", "This is the last section.");

    public static readonly Error NotReady = Error.Conflict("source.not_ready", "This source hasn't been read.");

    public static readonly Error TooLong = Error.Validation("source.section_too_long", "Together these sections are too long.");
}

/// <summary>
/// <c>/api/v1/ai/sources/{id}/…</c>: what the AI will be able to find in a source (PRD 4.7). Every
/// member reads it; Owner and Admin edit, exclude and merge. Saved directly until draft and publish
/// exist (Issue #55). The text is the shop's own and is returned as data: the dashboard shows it as
/// plain text, never as HTML (SEC-02).
/// </summary>
internal static class ReviewEndpoints
{
    public const int PageSize = 100;

    /// <summary>What a conflict check reads at most; a source's sections, 300 pages of them, fit well inside.</summary>
    private const int MaxSectionsChecked = 5000;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var sources = endpoints.MapGroup("/api/v1/ai/sources/{sourceId:guid}").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        // Costs an embedding per call, so limited and for those who manage knowledge, like the test chat.
        endpoints.MapPost("/api/v1/ai/sources/search", async (SearchTestRequest request, KnowledgeSearch search, CancellationToken ct) =>
            {
                var passages = await search.SearchAsync(request.Query!.Trim(), SearchScope.LiveAndReview, request.SourceId, ct);
                return Results.Ok(passages
                    .Select(p => new PassageResponse(p.SectionId, p.SourceId, p.SourceTitle, p.HeadingPath, p.Page, p.Text, p.Similarity))
                    .ToList());
            })
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>()
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<SearchTestRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);

        sources.MapPost("/use", (Guid sourceId, AiDbContext db, IAuditTrail audit, CancellationToken ct) =>
                SetUsedAsync(sourceId, true, db, audit, ct))
            .AddEndpointFilter(manage);

        sources.MapPost("/stop", (Guid sourceId, AiDbContext db, IAuditTrail audit, CancellationToken ct) =>
                SetUsedAsync(sourceId, false, db, audit, ct))
            .AddEndpointFilter(manage);

        sources.MapGet("/", async (Guid sourceId, AiDbContext db, IShippingRates shipping, CancellationToken ct) =>
        {
            var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sourceId, ct);
            if (source is null)
            {
                return SourceErrors.NotFound.ToProblem();
            }

            var sections = await db.Sections.AsNoTracking()
                .Where(s => s.SourceId == sourceId)
                .OrderBy(s => s.Position)
                .Take(MaxSectionsChecked)
                .ToListAsync(ct);
            // From the settings as they are now: fixing a setting clears its warning.
            var returnDays = await db.Policies.AsNoTracking().Select(p => p.ReturnDays).SingleOrDefaultAsync(ct);
            var facts = new BusinessFacts(await shipping.ListAsync(ct), returnDays);
            var conflicts = ConflictCheck.Find(sections, facts)
                .Select(c => new ConflictResponse(c.Kind, c.SectionId, c.Page, c.Found, c.Expected))
                .ToList();
            return Results.Ok(new SourceReviewResponse(SourceEndpoints.ToResponse(source, sections.Count), conflicts, ConflictCheck.PriceLists(sections)));
        });

        sources.MapGet("/sections", async (Guid sourceId, int? after, AiDbContext db, CancellationToken ct) =>
        {
            if (!await db.Sources.AnyAsync(s => s.Id == sourceId, ct))
            {
                return SourceErrors.NotFound.ToProblem();
            }

            var from = after ?? -1;
            var page = await db.Sections.AsNoTracking()
                .Where(s => s.SourceId == sourceId && s.Position > from)
                .OrderBy(s => s.Position)
                .Take(PageSize + 1)
                .ToListAsync(ct);
            var items = page.Take(PageSize).Select(ToResponse).ToList();
            return Results.Ok(new SectionPageResponse(items, page.Count > PageSize ? items[^1].Position : null));
        });

        sources.MapPut("/sections/{id:guid}", async (Guid sourceId, Guid id, SectionTextRequest request, AiDbContext db, SectionIndex index, IAuditTrail audit, CancellationToken ct) =>
            {
                var found = await FindAsync(db, sourceId, id, ct);
                if (found is null)
                {
                    return SourceErrors.NotFound.ToProblem();
                }

                var (source, section) = found.Value;
                var text = request.Text!.Trim();
                section.Edit(text, Chunker.Tokens(text), LanguageGuess.Of(text, section.Language));
                section.Hashed(SectionIndex.Hash(SectionIndex.EmbeddingText(source.Title, section)));
                await db.SaveChangesAsync(ct);
                var searchable = await index.ReindexAsync(source, section, ct);
                await audit.RecordAsync("knowledge.section_edited", ct);
                return Results.Ok(ToResponse(section) with { Searchable = searchable });
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<SectionTextRequest>>();

        sources.MapPost("/sections/{id:guid}/exclude", (Guid sourceId, Guid id, AiDbContext db, IAuditTrail audit, CancellationToken ct) =>
                SetExcludedAsync(sourceId, id, true, db, audit, ct))
            .AddEndpointFilter(manage);

        sources.MapPost("/sections/{id:guid}/include", (Guid sourceId, Guid id, AiDbContext db, IAuditTrail audit, CancellationToken ct) =>
                SetExcludedAsync(sourceId, id, false, db, audit, ct))
            .AddEndpointFilter(manage);

        sources.MapPost("/sections/{id:guid}/merge-next", async (Guid sourceId, Guid id, AiDbContext db, SectionIndex index, IAuditTrail audit, CancellationToken ct) =>
            {
                var found = await FindAsync(db, sourceId, id, ct);
                if (found is null)
                {
                    return SourceErrors.NotFound.ToProblem();
                }

                var (source, section) = found.Value;
                var next = await db.Sections
                    .Where(s => s.SourceId == sourceId && s.Position > section.Position)
                    .OrderBy(s => s.Position)
                    .FirstOrDefaultAsync(ct);
                if (next is null)
                {
                    return ReviewErrors.NoNextSection.ToProblem();
                }

                if (section.Text.Length + next.Text.Length + 2 > KnowledgeSection.TextMaxLength)
                {
                    return ReviewErrors.TooLong.ToProblem();
                }

                section.Merge(next, Chunker.Tokens(section.Text + next.Text));
                section.Hashed(SectionIndex.Hash(SectionIndex.EmbeddingText(source.Title, section)));
                db.Sections.Remove(next);
                await db.SaveChangesAsync(ct);
                var searchable = await index.ReindexAsync(source, section, ct);
                await audit.RecordAsync("knowledge.sections_merged", ct);
                return Results.Ok(ToResponse(section) with { Searchable = searchable });
            })
            .AddEndpointFilter(manage);
    }

    private static async Task<IResult> SetUsedAsync(Guid sourceId, bool used, AiDbContext db, IAuditTrail audit, CancellationToken ct)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null)
        {
            return SourceErrors.NotFound.ToProblem();
        }

        if (!(used ? source.Use() : source.StopUsing()))
        {
            return ReviewErrors.NotReady.ToProblem();
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(used ? "knowledge.source_used" : "knowledge.source_stopped", ct);
        var sections = await db.Sections.CountAsync(s => s.SourceId == sourceId, ct);
        return Results.Ok(SourceEndpoints.ToResponse(source, sections));
    }

    private static async Task<IResult> SetExcludedAsync(Guid sourceId, Guid id, bool excluded, AiDbContext db, IAuditTrail audit, CancellationToken ct)
    {
        var found = await FindAsync(db, sourceId, id, ct);
        if (found is null)
        {
            return SourceErrors.NotFound.ToProblem();
        }

        found.Value.Section.SetExcluded(excluded);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(excluded ? "knowledge.section_excluded" : "knowledge.section_included", ct);
        return Results.Ok(ToResponse(found.Value.Section));
    }

    /// <summary>Both through the tenant-scoped context: another workspace's IDs are simply not found (SEC-06).</summary>
    private static async Task<(KnowledgeSource Source, KnowledgeSection Section)?> FindAsync(AiDbContext db, Guid sourceId, Guid id, CancellationToken ct)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == sourceId, ct);
        var section = source is null ? null : await db.Sections.SingleOrDefaultAsync(s => s.Id == id && s.SourceId == sourceId, ct);
        return section is null ? null : (source!, section);
    }

    private static SectionResponse ToResponse(KnowledgeSection s) => new(
        s.Id,
        s.Position,
        s.HeadingPath,
        s.Page,
        s.Kind.ToString().ToLowerInvariant(),
        s.Text,
        s.Excluded,
        s.EmbeddingModel is not null);
}
