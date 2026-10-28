using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Features.Knowledge;

internal sealed record QuestionResponse(Guid Id, Guid ConversationId, string Question, string Status, Guid? AnswerId, DateTimeOffset CreatedAt);

internal sealed record AnswerResponse(Guid Id, string Question, string Answer, bool Active, DateTimeOffset UpdatedAt);

/// <summary>Teach the AI: the answer, and optionally a cleaner wording of the question.</summary>
internal sealed record TeachRequest(string? Question, string? Answer);

internal sealed record AnswerRequest(string? Question, string? Answer, bool? Active);

internal sealed class TeachValidator : AbstractValidator<TeachRequest>
{
    public TeachValidator()
    {
        RuleFor(r => r.Question).MaximumLength(TaughtAnswer.QuestionMaxLength);
        RuleFor(r => r.Answer).NotEmpty().Must(a => !string.IsNullOrWhiteSpace(a)).MaximumLength(TaughtAnswer.AnswerMaxLength);
    }
}

internal sealed class AnswerValidator : AbstractValidator<AnswerRequest>
{
    public AnswerValidator()
    {
        RuleFor(r => r.Question).NotEmpty().Must(q => !string.IsNullOrWhiteSpace(q)).MaximumLength(TaughtAnswer.QuestionMaxLength);
        RuleFor(r => r.Answer).NotEmpty().Must(a => !string.IsNullOrWhiteSpace(a)).MaximumLength(TaughtAnswer.AnswerMaxLength);
    }
}

internal static class KnowledgeErrors
{
    /// <summary>Also for other workspaces' IDs (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("ai.not_found", "Not found.");

    public static readonly Error AlreadyResolved = Error.Conflict("ai.question_resolved", "This question was already answered or dismissed.");
}

/// <summary>
/// <c>/api/v1/ai/questions</c> and <c>/api/v1/ai/answers</c> (ADR 0019): every member sees what the
/// AI couldn't answer; Owner and Admin teach it (answers change what the AI tells customers).
/// </summary>
internal static class KnowledgeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var ai = endpoints.MapGroup("/api/v1/ai").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        ai.MapGet("/questions", async (string? status, AiDbContext db, CancellationToken ct) =>
        {
            var wanted = status switch
            {
                null or "open" => GapStatus.Open,
                "answered" => GapStatus.Answered,
                "dismissed" => GapStatus.Dismissed,
                _ => (GapStatus?)null,
            };
            if (wanted is null)
            {
                return Error.Validation("validation.failed", "Status must be open, answered or dismissed.").ToProblem();
            }

            var gaps = await db.Gaps.AsNoTracking()
                .Where(g => g.Status == wanted)
                .OrderByDescending(g => g.CreatedAt)
                .Take(200)
                .Select(g => new QuestionResponse(g.Id, g.ConversationId, g.Question, g.Status.ToString().ToLowerInvariant(), g.AnswerId, g.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(gaps);
        });

        ai.MapPost("/questions/{id:guid}/answer", async (Guid id, TeachRequest request, AiDbContext db, ITeamDirectory team, ITenantContext tenant, CancellationToken ct) =>
            {
                var gap = await db.Gaps.SingleOrDefaultAsync(g => g.Id == id, ct);
                if (gap is null)
                {
                    return KnowledgeErrors.NotFound.ToProblem();
                }

                if (gap.Status != GapStatus.Open)
                {
                    return KnowledgeErrors.AlreadyResolved.ToProblem();
                }

                var me = (await team.CurrentAsync(ct))!;
                var answer = TaughtAnswer.Create(tenant.TenantId, string.IsNullOrWhiteSpace(request.Question) ? gap.Question : request.Question, request.Answer!, me.UserId);
                db.Answers.Add(answer);
                gap.Answered(answer.Id, me.UserId);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(answer));
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<TeachRequest>>();

        ai.MapPost("/questions/{id:guid}/dismiss", async (Guid id, AiDbContext db, ITeamDirectory team, CancellationToken ct) =>
            {
                var gap = await db.Gaps.SingleOrDefaultAsync(g => g.Id == id, ct);
                if (gap is null)
                {
                    return KnowledgeErrors.NotFound.ToProblem();
                }

                if (gap.Status != GapStatus.Open)
                {
                    return KnowledgeErrors.AlreadyResolved.ToProblem();
                }

                gap.Dismiss((await team.CurrentAsync(ct))!.UserId);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .AddEndpointFilter(manage);

        ai.MapGet("/answers", async (AiDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Answers.AsNoTracking()
                .OrderByDescending(a => a.UpdatedAt)
                .Take(500)
                .Select(a => new AnswerResponse(a.Id, a.Question, a.Answer, a.Active, a.UpdatedAt))
                .ToListAsync(ct)));

        ai.MapPost("/answers", async (AnswerRequest request, AiDbContext db, ITeamDirectory team, ITenantContext tenant, CancellationToken ct) =>
            {
                var answer = TaughtAnswer.Create(tenant.TenantId, request.Question!, request.Answer!, (await team.CurrentAsync(ct))!.UserId);
                db.Answers.Add(answer);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/v1/ai/answers/{answer.Id}", ToResponse(answer));
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<AnswerRequest>>();

        ai.MapPut("/answers/{id:guid}", async (Guid id, AnswerRequest request, AiDbContext db, ITeamDirectory team, CancellationToken ct) =>
            {
                var answer = await db.Answers.SingleOrDefaultAsync(a => a.Id == id, ct);
                if (answer is null)
                {
                    return KnowledgeErrors.NotFound.ToProblem();
                }

                answer.Update(request.Question!, request.Answer!, request.Active ?? answer.Active, (await team.CurrentAsync(ct))!.UserId);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(answer));
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<AnswerRequest>>();
    }

    private static AnswerResponse ToResponse(TaughtAnswer a) => new(a.Id, a.Question, a.Answer, a.Active, a.UpdatedAt);
}
