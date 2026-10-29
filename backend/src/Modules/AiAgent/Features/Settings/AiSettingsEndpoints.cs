using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Features.Settings;

internal sealed record AiSettingsResponse(
    bool Enabled,
    string Tone,
    string? Instructions,
    string? Disclosure,
    string DefaultDisclosure,
    // False until the server has an API key: then the AI stays off whatever the setting.
    bool ProviderConfigured);

internal sealed record AiSettingsRequest(bool Enabled, string? Tone, string? Instructions, string? Disclosure);

internal sealed record TestMessage(string? Author, string? Text);

/// <summary>A conversation typed in the dashboard; nothing is stored or sent to any customer.</summary>
internal sealed record TestChatRequest(List<TestMessage>? Messages);

internal sealed record TestChatResponse(
    string? Disclosure,
    string? Reply,
    string? Handoff,
    string? Question,
    string? Blocked,
    // Why the provider failed, e.g. http_401 (key rejected), http_429, output_limit.
    string? Failed,
    IReadOnlyList<string> Tools,
    IReadOnlyList<TestSource> Sources);

/// <summary>
/// What an answer was based on (PRD 4.7): <c>shipping</c> (the zones), <c>policy</c> with the
/// topic as label, or <c>answer</c> with the answer's question as label.
/// </summary>
internal sealed record TestSource(string Kind, string? Label);

internal sealed class AiSettingsValidator : AbstractValidator<AiSettingsRequest>
{
    public AiSettingsValidator()
    {
        RuleFor(r => r.Tone).Must(t => t is null or "friendly" or "formal").WithMessage("Tone must be friendly or formal.");
        RuleFor(r => r.Instructions).MaximumLength(AiSettings.InstructionsMaxLength);
        RuleFor(r => r.Disclosure).MaximumLength(AiSettings.DisclosureMaxLength);
    }
}

internal sealed class TestChatValidator : AbstractValidator<TestChatRequest>
{
    public TestChatValidator()
    {
        RuleFor(r => r.Messages).NotEmpty().Must(m => m is null || m.Count <= 20).WithMessage("1 to 20 messages.");
        RuleFor(r => r.Messages).Must(m => m is null || (m.Count > 0 && m[^1].Author == "customer"))
            .WithMessage("The last message must be the customer's.");
        RuleForEach(r => r.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Author).Must(a => a is "customer" or "ai").WithMessage("Author must be customer or ai.");
            message.RuleFor(m => m.Text).NotEmpty().MaximumLength(1000);
        });
    }
}

internal static class AiSettingsErrors
{
    public static readonly Error NotConfigured =
        Error.Conflict("ai.not_configured", "The AI isn't set up on this server yet (no API key).");

    public static readonly Error TestLimit =
        Error.Conflict("ai.test_limit", "Too many test messages in the last hour. Please try again later.");
}

/// <summary>
/// <c>/api/v1/ai/settings</c> and <c>/api/v1/ai/test</c> (PRD 4.8). Everyone sees the settings;
/// Owner and Admin change them and use the test chat (it costs tokens: metered and limited, SEC-39).
/// </summary>
internal static class AiSettingsEndpoints
{
    public const int TestChatsPerHour = 30;
    public const string TestChatDetail = "test_chat";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var ai = endpoints.MapGroup("/api/v1/ai").RequireAuthorization().AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        ai.MapGet("/settings", async (AiSettingsStore store, ILlmProvider llm, CancellationToken ct) =>
            Results.Ok(ToResponse(await store.GetAsync(ct), await store.ShopNameAsync(ct), llm.IsConfigured)));

        ai.MapPut("/settings", async (AiSettingsRequest request, AiDbContext db, AiSettingsStore store, ILlmProvider llm, ITenantContext tenant, CancellationToken ct) =>
            {
                var settings = await db.Settings.SingleOrDefaultAsync(ct);
                if (settings is null)
                {
                    settings = AiSettings.Defaults(tenant.TenantId);
                    db.Settings.Add(settings);
                }

                settings.Update(request.Enabled, request.Tone == "formal" ? AiTone.Formal : AiTone.Friendly, request.Instructions, request.Disclosure);
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(settings, await store.ShopNameAsync(ct), llm.IsConfigured));
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<AiSettingsRequest>>();

        ai.MapPost("/test", async (
                TestChatRequest request,
                AgentBrain brain,
                AiSettingsStore store,
                ILlmProvider llm,
                AiDbContext db,
                ITenantContext tenant,
                IOptions<AiOptions> options,
                TimeProvider time,
                CancellationToken ct) =>
            {
                if (!llm.IsConfigured)
                {
                    return AiSettingsErrors.NotConfigured.ToProblem();
                }

                var hourAgo = time.GetUtcNow().AddHours(-1);
                if (await db.Runs.CountAsync(r => r.Detail == TestChatDetail && r.CreatedAt > hourAgo, ct) >= TestChatsPerHour)
                {
                    return AiSettingsErrors.TestLimit.ToProblem();
                }

                var watch = Stopwatch.StartNew();
                var settings = await store.GetAsync(ct);
                var shop = await store.ShopNameAsync(ct);
                var now = time.GetUtcNow();
                var history = request.Messages!.Select(m => new AiHistoryMessage(m.Author!, m.Text!.Trim(), now)).ToList();
                // The same brain as real conversations, with settings as saved; nothing is stored but the metering.
                var thought = await brain.ThinkAsync(Prompt.System(shop, settings, null, null), history, ct);

                db.Runs.Add(AiRun.Finish(
                    tenant.TenantId, Guid.Empty, options.Value.ReplyModel, thought.InputTokens, thought.OutputTokens, thought.Tools,
                    thought.Reply is not null ? RunOutcome.Replied : RunOutcome.HandedOff, TestChatDetail, watch.Elapsed, thought.Sources));
                await db.SaveChangesAsync(ct);
                var sources = await SourcesAsync(thought.Sources, db, ct);

                var first = history.All(m => m.Author == "customer");
                return Results.Ok(new TestChatResponse(
                    first && thought.Reply is not null ? Prompt.Disclosure(shop, settings) : null,
                    thought.Reply,
                    thought.HandoffReason ?? (thought.Failed is null ? null : "ai_unavailable"),
                    thought.Question,
                    thought.Blocked,
                    thought.Failed,
                    thought.Tools,
                    sources));
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<TestChatRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);
    }

    private static async Task<List<TestSource>> SourcesAsync(IReadOnlyList<string> codes, AiDbContext db, CancellationToken ct)
    {
        var answerIds = codes
            .Where(c => c.StartsWith(KnowledgeSources.AnswerPrefix, StringComparison.Ordinal))
            .Select(c => Guid.TryParse(c[KnowledgeSources.AnswerPrefix.Length..], out var id) ? id : Guid.Empty)
            .ToList();
        var questions = await db.Answers.AsNoTracking()
            .Where(a => answerIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Question, ct);

        return codes.Select(code => code switch
            {
                KnowledgeSources.Shipping => new TestSource("shipping", null),
                _ when code.StartsWith(KnowledgeSources.PolicyPrefix, StringComparison.Ordinal) =>
                    new TestSource("policy", code[KnowledgeSources.PolicyPrefix.Length..]),
                _ => new TestSource("answer", Guid.TryParse(code[KnowledgeSources.AnswerPrefix.Length..], out var id) ? questions.GetValueOrDefault(id) : null),
            })
            .ToList();
    }

    private static AiSettingsResponse ToResponse(AiSettings s, string shop, bool configured) => new(
        s.Enabled,
        s.Tone.ToString().ToLowerInvariant(),
        s.Instructions,
        s.Disclosure,
        Prompt.DefaultDisclosure(shop),
        configured);
}
