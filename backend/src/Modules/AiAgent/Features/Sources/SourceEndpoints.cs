using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.AiAgent.Features.Sources;

internal sealed record SourceResponse(
    Guid Id,
    string Kind,
    string Title,
    string Origin,
    string Status,
    string? FailureReason,
    long? SizeBytes,
    int SectionCount,
    DateTimeOffset? LastReadAt,
    DateTimeOffset CreatedAt);

internal sealed record WebPageRequest(string? Url);

internal sealed class WebPageValidator : AbstractValidator<WebPageRequest>
{
    public WebPageValidator() =>
        RuleFor(r => r.Url).NotEmpty().MaximumLength(KnowledgeSource.OriginMaxLength);
}

internal static class SourceErrors
{
    /// <summary>Also for other workspaces' sources (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("source.not_found", "Source not found.");

    public static readonly Error NoFile = Error.Validation("source.no_file", "Choose a file.");

    public static readonly Error TooLarge = Error.Validation("source.too_large", "The file is larger than 20 MB.");

    public static readonly Error Unsupported = Error.Validation("source.unsupported_type", "Upload a PDF, Word (.docx) or text file.");

    public static readonly Error LimitReached = Error.Conflict("source.limit_reached", "This workspace has reached 100 sources.");

    public static readonly Error Duplicate = Error.Conflict("source.duplicate", "This file or page was already added.");

    public static readonly Error InvalidUrl = Error.Validation("source.invalid_url", "Enter a full web address (http or https).");

    public static readonly Error BlockedUrl = Error.Validation("source.blocked_url", "This address isn't a public web page.");

    public static readonly Error UnreachableUrl = Error.Validation("source.unreachable_url", "This address can't be found.");
}

/// <summary>Works out what a file is from its content, never from its name or the browser's type (SEC-04).</summary>
internal static class FileKind
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static SourceKind? Detect(byte[] content, string fileName)
    {
        if (content.AsSpan().StartsWith("%PDF-"u8))
        {
            return SourceKind.Pdf;
        }

        if (content.AsSpan().StartsWith("PK\u0003\u0004"u8))
        {
            return IsDocx(content) ? SourceKind.Docx : null;
        }

        return fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && IsText(content) ? SourceKind.Txt : null;
    }

    /// <summary>Only lists the ZIP's entries; nothing is unpacked here.</summary>
    private static bool IsDocx(byte[] content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            return zip.GetEntry("word/document.xml") is not null && zip.GetEntry("[Content_Types].xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool IsText(byte[] content)
    {
        if (Array.IndexOf(content, (byte)0) >= 0)
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

/// <summary>
/// <c>/api/v1/ai/sources</c> (PRD 4.7, ADR 0026 §2): every member sees the sources, Owner and Admin
/// add and delete them (they change what the AI can tell customers). A background job reads them.
/// </summary>
internal static class SourceEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var sources = endpoints.MapGroup("/api/v1/ai/sources").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        sources.MapGet("/", async (AiDbContext db, CancellationToken ct) =>
        {
            var list = await db.Sources.AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .Take(KnowledgeSource.MaxPerWorkspace)
                .Select(s => new { Source = s, Sections = db.Sections.Count(x => x.SourceId == s.Id) })
                .ToListAsync(ct);
            return Results.Ok(list.Select(x => ToResponse(x.Source, x.Sections)).ToList());
        });

        // The form is read by hand (not bound to IFormFile), so the API's own CSRF check applies (SEC-03).
        sources.MapPost("/files", UploadAsync)
            .AddEndpointFilter(manage)
            .WithMetadata(new RequestSizeLimitAttribute(KnowledgeSource.MaxFileBytes + 64 * 1024))
            .RequireRateLimiting(RatePolicies.Uploads);

        sources.MapPost("/web", AddWebPageAsync)
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<WebPageRequest>>()
            .RequireRateLimiting(RatePolicies.Uploads);

        sources.MapDelete("/{id:guid}", async (Guid id, AiDbContext db, IFileStore files, IAuditTrail audit, CancellationToken ct) =>
            {
                var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == id, ct);
                if (source is null)
                {
                    return SourceErrors.NotFound.ToProblem();
                }

                // Sections and their search data go with it (cascade, SEC-45); the file after the commit.
                db.Sources.Remove(source);
                await db.SaveChangesAsync(ct);
                if (source.StorageKey is { } key)
                {
                    await files.DeleteAsync(key, ct);
                }

                await audit.RecordAsync("knowledge.source_deleted", ct);
                return Results.NoContent();
            })
            .AddEndpointFilter(manage);
    }

    private static async Task<IResult> UploadAsync(
        HttpRequest request, AiDbContext db, IFileStore files, ITeamDirectory team, ITenantContext tenant, IAuditTrail audit, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return SourceErrors.NoFile.ToProblem();
        }

        var form = await request.ReadFormAsync(ct);
        if (form.Files.GetFile("file") is not { Length: > 0 } file)
        {
            return SourceErrors.NoFile.ToProblem();
        }

        if (file.Length > KnowledgeSource.MaxFileBytes)
        {
            return SourceErrors.TooLarge.ToProblem();
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        var name = CleanFileName(file.FileName);
        if (FileKind.Detect(content, name) is not { } kind)
        {
            return SourceErrors.Unsupported.ToProblem();
        }

        if (await db.Sources.CountAsync(ct) >= KnowledgeSource.MaxPerWorkspace)
        {
            return SourceErrors.LimitReached.ToProblem();
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        if (await db.Sources.AnyAsync(s => s.ContentHash == hash, ct))
        {
            return SourceErrors.Duplicate.ToProblem();
        }

        var me = (await team.CurrentAsync(ct))!;
        buffer.Position = 0;
        var key = await files.SaveAsync(tenant.TenantId, buffer, ct);
        var source = KnowledgeSource.FromFile(tenant.TenantId, kind, name, key, hash, content.Length, me.UserId);
        db.Sources.Add(source);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two uploads of the same file at once: the unique index decides.
            await files.DeleteAsync(key, ct);
            return SourceErrors.Duplicate.ToProblem();
        }

        await audit.RecordAsync("knowledge.source_added", ct);
        return Results.Ok(ToResponse(source, 0));
    }

    private static async Task<IResult> AddWebPageAsync(
        WebPageRequest request, AiDbContext db, SafeHttpClient http, ITeamDirectory team, ITenantContext tenant, IAuditTrail audit, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url!.Trim(), UriKind.Absolute, out var url))
        {
            return SourceErrors.InvalidUrl.ToProblem();
        }

        try
        {
            await http.CheckAsync(url, ct);
        }
        catch (SafeHttpException ex)
        {
            return (ex.Failure switch
            {
                SafeHttpFailure.BlockedAddress => SourceErrors.BlockedUrl,
                SafeHttpFailure.Unreachable => SourceErrors.UnreachableUrl,
                _ => SourceErrors.InvalidUrl,
            }).ToProblem();
        }

        if (await db.Sources.CountAsync(ct) >= KnowledgeSource.MaxPerWorkspace)
        {
            return SourceErrors.LimitReached.ToProblem();
        }

        var origin = url.AbsoluteUri;
        if (await db.Sources.AnyAsync(s => s.Kind == SourceKind.WebPage && s.Origin == origin, ct))
        {
            return SourceErrors.Duplicate.ToProblem();
        }

        var me = (await team.CurrentAsync(ct))!;
        var source = KnowledgeSource.FromWebPage(tenant.TenantId, origin, me.UserId);
        db.Sources.Add(source);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return SourceErrors.Duplicate.ToProblem();
        }

        await audit.RecordAsync("knowledge.source_added", ct);
        return Results.Ok(ToResponse(source, 0));
    }

    /// <summary>For showing only: the last part of the name, no control characters (SEC-04).</summary>
    internal static string CleanFileName(string? name)
    {
        var last = (name ?? string.Empty).Replace('\\', '/').Split('/')[^1];
        var clean = new string(last.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length == 0 ? "file" : clean;
    }

    private static SourceResponse ToResponse(KnowledgeSource s, int sections) => new(
        s.Id,
        s.Kind.ToString().ToLowerInvariant(),
        s.Title,
        s.Origin,
        s.Status.ToString().ToLowerInvariant(),
        s.FailureReason,
        s.SizeBytes,
        sections,
        s.LastReadAt,
        s.CreatedAt);
}
