using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>
/// What the AI decided in one turn: a checked reply, a handoff, a held-back draft, or a provider
/// failure. Nothing is stored or sent here.
/// </summary>
internal sealed record Thought(
    string? Reply,
    string? HandoffReason,
    string? Question,
    string? Blocked,
    string? Failed,
    int InputTokens,
    int OutputTokens,
    IReadOnlyList<string> Tools,
    // What the answer was based on, see KnowledgeSources.
    IReadOnlyList<string> Sources,
    // Tool calls and the reply check, for "Why this answer" (ADR 0026 §6).
    RunTrace Trace);

/// <summary>
/// The thinking part of a turn (ADR 0019): prompt, model, tools, reply check. Shared by real
/// conversations and the dashboard's test chat, so both behave exactly alike.
/// </summary>
internal sealed class AgentBrain(ILlmProvider llm, AgentTools tools, IOptions<AiOptions> options)
{
    private AiOptions Options => options.Value;

    /// <summary>
    /// <paramref name="readOnly"/>: the team's assistant (ADR 0023) gets only the tools that read.
    /// <paramref name="summary"/>: the conversation's older part; <paramref name="history"/> is the part within the budget.
    /// </summary>
    public async Task<Thought> ThinkAsync(
        string systemPrompt, IReadOnlyList<AiHistoryMessage> history, CancellationToken ct, bool readOnly = false, string? summary = null, string? orderSoFar = null)
    {
        var messages = new List<LlmMessage> { LlmMessage.System(systemPrompt) };
        if (summary is not null)
        {
            messages.Add(LlmMessage.System(Prompt.Memory(summary)));
        }

        // Written by code from the catalog (ADR 0032 §4), so it is shop data, not customer text.
        if (orderSoFar is not null)
        {
            messages.Add(LlmMessage.System(orderSoFar));
        }

        messages.AddRange(history.Select(m => m.Author == "customer" ? LlmMessage.User(m.Text) : LlmMessage.Assistant(m.Text)));

        // The team's assistant is left out: its summary is in the team's language, not the customer's.
        var language = readOnly ? null : ReplyLanguage.OfCustomer(history);
        if (language is not null)
        {
            messages.Add(LlmMessage.System(Prompt.ReplyIn(language)));
        }

        var offered = readOnly ? AgentTools.ReadOnlyDefinitions : AgentTools.Definitions;
        var usedTools = new List<string>();
        var calls = new List<ToolTrace>();
        string? check = null;
        int input = 0, output = 0;
        Thought Done(string? reply = null, string? handoff = null, string? question = null, string? blocked = null, string? failed = null) =>
            new(reply, handoff, question, blocked, failed, input, output, usedTools, [.. tools.Sources], new RunTrace(calls, check));

        for (var round = 0; round <= Options.MaxToolRounds; round++)
        {
            LlmResponse response;
            try
            {
                response = await llm.CompleteAsync(
                    new LlmRequest(Options.ReplyModel, messages, round < Options.MaxToolRounds ? offered : [], Options.MaxOutputTokens),
                    ct);
            }
            catch (LlmException ex)
            {
                return Done(failed: ex.Reason);
            }

            input += response.Usage.InputTokens;
            output += response.Usage.OutputTokens;
            if (response.ToolCalls.Count == 0)
            {
                var reply = response.Text?.Trim();
                if (language is not null && ReplyLanguage.HasOtherLanguageSentence(reply, language))
                {
                    reply = await InCustomersLanguageAsync(reply!, language, ct);
                }

                // Products belong in cards, not in text (ADR 0032 §5); the team's assistant can't send cards.
                var blocked = Guardrails.Check(reply, tools.AllowedPrices)
                    ?? (!readOnly && tools.ProductsNamedIn(reply!) > Guardrails.MaxProductsInText ? "products_in_text" : null);
                check = blocked ?? RunTraces.Passed;
                return blocked is not null ? Done(blocked: blocked) : Done(reply: reply);
            }

            messages.Add(new LlmMessage(LlmRole.Assistant, response.Text, response.ToolCalls));
            foreach (var call in response.ToolCalls)
            {
                usedTools.Add(call.Name.Length <= 40 ? call.Name : call.Name[..40]);
                var outcome = await tools.RunAsync(call, ct, readOnly);
                calls.Add(RunTraces.Of(call, outcome));
                if (outcome.HandoffReason is { } reason)
                {
                    return Done(handoff: reason, question: outcome.Question);
                }

                messages.Add(LlmMessage.ToolResult(call.Id, outcome.Json));
            }
        }

        check = "too_many_tool_rounds";
        return Done(blocked: check);

        // One try, and the reply check still follows. If it fails, a mixed reply is better than no reply.
        async Task<string> InCustomersLanguageAsync(string mixed, string target, CancellationToken cancel)
        {
            calls.Add(new ToolTrace("fix_reply_language", target, string.Empty));
            try
            {
                var fixedReply = await llm.CompleteAsync(
                    new LlmRequest(Options.ReplyModel, [LlmMessage.System(Prompt.LanguageFix(target)), LlmMessage.User(mixed)], [], Options.MaxOutputTokens),
                    cancel);
                input += fixedReply.Usage.InputTokens;
                output += fixedReply.Usage.OutputTokens;
                return string.IsNullOrWhiteSpace(fixedReply.Text) ? mixed : fixedReply.Text.Trim();
            }
            catch (LlmException)
            {
                return mixed;
            }
        }
    }
}

/// <summary>The workspace's AI settings and shop name, with defaults until an owner saves.</summary>
internal sealed class AiSettingsStore(AiDbContext db, ITenantContext tenant, IWorkspaceInfo workspace)
{
    public async Task<AiSettings> GetAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? AiSettings.Defaults(tenant.TenantId);

    public async Task<string> ShopNameAsync(CancellationToken ct) => await workspace.NameAsync(ct) ?? "the shop";
}
