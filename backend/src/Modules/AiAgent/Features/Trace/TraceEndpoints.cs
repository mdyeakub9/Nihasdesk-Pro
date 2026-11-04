using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Features.Settings;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Features.Trace;

internal sealed record ToolTraceResponse(string Name, string Arguments, string Result);

/// <summary>
/// "Why this answer" (PRD 4.8, ADR 0026 §6). Without <see cref="HasDetails"/> the tool calls are gone
/// (older than the retention period, or from before tracing); the summary (model, tools, sources) stays.
/// </summary>
internal sealed record TraceResponse(
    string Outcome,
    string? Detail,
    string Model,
    string? InstructionVersion,
    int InputTokens,
    int OutputTokens,
    int DurationMs,
    IReadOnlyList<TestSource> Sources,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<ToolTraceResponse> Tools,
    string? Check,
    bool HasDetails,
    DateTimeOffset CreatedAt,
    string? MyRating);

/// <summary>An AI reply of a conversation that has a trace, with the current member's rating.</summary>
internal sealed record RatedReplyResponse(Guid MessageId, string? Rating);

/// <summary><c>up</c>, <c>down</c>, or null to take the rating back.</summary>
internal sealed record FeedbackRequest(string? Rating);

/// <summary>
/// A 👎 reply as an eval case to review (PRD 4.8). It contains the customer's words: the owner
/// removes names, numbers and addresses before adding it to the eval set (GDPR).
/// </summary>
internal sealed record EvalCaseExport(string Id, string? Question, string? AiReply, string? ExpectedAnswer, bool? ExpectHandoff, DateTimeOffset RatedAt);

internal sealed record FeedbackExportResponse(string Notice, IReadOnlyList<EvalCaseExport> Cases);

internal sealed class FeedbackValidator : AbstractValidator<FeedbackRequest>
{
    public FeedbackValidator() =>
        RuleFor(r => r.Rating).Must(r => r is null or "up" or "down").WithMessage("Rating must be up, down or empty.");
}

internal static class TraceErrors
{
    /// <summary>Also for other workspaces' messages and messages the AI didn't write (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("ai.not_found", "Not found.");
}

/// <summary>
/// <c>/api/v1/ai/messages/{id}/…</c>: every member sees why the AI answered and rates replies;
/// Owner and Admin export 👎 replies for the eval set.
/// </summary>
internal static class TraceEndpoints
{
    public const int ExportLimit = 200;
    private const int ExportHistorySize = 100;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var ai = endpoints.MapGroup("/api/v1/ai").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        ai.MapGet("/messages/{id:guid}/trace", async (Guid id, AiDbContext db, ITeamDirectory team, CancellationToken ct) =>
        {
            var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.MessageId == id, ct);
            if (run is null)
            {
                return TraceErrors.NotFound.ToProblem();
            }

            var me = (await team.CurrentAsync(ct))!;
            var rating = await db.Feedback.AsNoTracking()
                .Where(f => f.MessageId == id && f.UserId == me.UserId)
                .Select(f => (Rating?)f.Rating)
                .SingleOrDefaultAsync(ct);
            return Results.Ok(await ToResponseAsync(run, rating, db, ct));
        });

        ai.MapGet("/conversations/{id:guid}/replies", async (Guid id, AiDbContext db, ITeamDirectory team, CancellationToken ct) =>
        {
            var me = (await team.CurrentAsync(ct))!;
            var messageIds = await db.Runs.AsNoTracking()
                .Where(r => r.ConversationId == id && r.MessageId != null)
                .OrderBy(r => r.CreatedAt)
                .Select(r => r.MessageId!.Value)
                .Take(500)
                .ToListAsync(ct);
            var ratings = await db.Feedback.AsNoTracking()
                .Where(f => f.UserId == me.UserId && messageIds.Contains(f.MessageId))
                .ToDictionaryAsync(f => f.MessageId, f => f.Rating, ct);
            return Results.Ok(messageIds
                .Select(m => new RatedReplyResponse(m, ratings.TryGetValue(m, out var r) ? Text(r) : null))
                .ToList());
        });

        ai.MapPut("/messages/{id:guid}/feedback", async (Guid id, FeedbackRequest request, AiDbContext db, ITeamDirectory team, ITenantContext tenant, CancellationToken ct) =>
            {
                var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.MessageId == id, ct);
                if (run is null)
                {
                    return TraceErrors.NotFound.ToProblem();
                }

                var me = (await team.CurrentAsync(ct))!;
                var feedback = await db.Feedback.SingleOrDefaultAsync(f => f.MessageId == id && f.UserId == me.UserId, ct);
                var rating = request.Rating switch
                {
                    "up" => Rating.Up,
                    "down" => Rating.Down,
                    _ => (Rating?)null,
                };

                if (rating is null)
                {
                    if (feedback is not null)
                    {
                        db.Feedback.Remove(feedback);
                    }
                }
                else if (feedback is null)
                {
                    db.Feedback.Add(ReplyFeedback.Create(tenant.TenantId, id, run.Id, me.UserId, rating.Value));
                }
                else
                {
                    feedback.Change(rating.Value);
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(new RatedReplyResponse(id, rating is { } r ? Text(r) : null));
            })
            .AddEndpointFilter<ValidationFilter<FeedbackRequest>>();

        ai.MapGet("/feedback/export", async (AiDbContext db, IAiConversations conversations, CancellationToken ct) =>
            {
                var down = await db.Feedback.AsNoTracking()
                    .Where(f => f.Rating == Rating.Down)
                    .OrderByDescending(f => f.CreatedAt)
                    .Take(ExportLimit)
                    .ToListAsync(ct);
                var messageIds = down.Select(f => f.MessageId).Distinct().ToList();
                var runs = await db.Runs.AsNoTracking()
                    .Where(r => r.MessageId != null && messageIds.Contains(r.MessageId.Value))
                    .ToDictionaryAsync(r => r.MessageId!.Value, ct);

                var cases = new List<EvalCaseExport>();
                var histories = new Dictionary<Guid, AiConversation?>();
                foreach (var feedback in down.DistinctBy(f => f.MessageId))
                {
                    if (!runs.TryGetValue(feedback.MessageId, out var run))
                    {
                        continue;
                    }

                    if (!histories.TryGetValue(run.ConversationId, out var history))
                    {
                        // The AI's own view of the chat: never internal notes or team names (SEC-37).
                        history = await conversations.HistoryAsync(run.ConversationId, ExportHistorySize, ct);
                        histories[run.ConversationId] = history;
                    }

                    var (question, reply) = Around(history, run.CreatedAt);
                    cases.Add(new EvalCaseExport($"feedback-{feedback.MessageId:N}"[..17], question, reply, null, null, feedback.CreatedAt));
                }

                return Results.Ok(new FeedbackExportResponse(
                    "Contains customers' words. Remove names, phone numbers, emails and addresses, write the expected answer or set expectHandoff, then add the case to the eval set.",
                    cases));
            })
            .AddEndpointFilter(manage);
    }

    /// <summary>The AI reply sent by the run and the customer message it answered (by time; the history has no IDs).</summary>
    internal static (string? Question, string? Reply) Around(AiConversation? history, DateTimeOffset runAt)
    {
        if (history is null)
        {
            return (null, null);
        }

        var before = history.Messages.Where(m => m.SentAt <= runAt).ToList();
        var replyIndex = before.FindLastIndex(m => m.Author == "ai");
        if (replyIndex < 0)
        {
            return (null, null);
        }

        var question = before.Take(replyIndex).LastOrDefault(m => m.Author == "customer")?.Text;
        return (question, before[replyIndex].Text);
    }

    private static async Task<TraceResponse> ToResponseAsync(AiRun run, Rating? rating, AiDbContext db, CancellationToken ct) => new(
        run.Outcome.ToString().ToLowerInvariant(),
        run.Detail,
        run.Model,
        run.InstructionVersion,
        run.InputTokens,
        run.OutputTokens,
        run.DurationMs,
        await AiSettingsEndpoints.SourcesAsync(run.Sources, db, ct),
        run.Tools,
        run.Trace?.Tools.Select(t => new ToolTraceResponse(t.Name, t.Arguments, t.Result)).ToList() ?? [],
        run.Trace?.Check,
        run.Trace is not null,
        run.CreatedAt,
        rating is { } r ? Text(r) : null);

    /// <summary>The trace for a run that was just made (test chat): same shape as a stored one.</summary>
    internal static Task<TraceResponse> ForAsync(AiRun run, AiDbContext db, CancellationToken ct) => ToResponseAsync(run, null, db, ct);

    private static string Text(Rating rating) => rating.ToString().ToLowerInvariant();
}
