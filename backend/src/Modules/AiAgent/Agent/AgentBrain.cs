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
    IReadOnlyList<string> Sources);

/// <summary>
/// The thinking part of a turn (ADR 0019): prompt, model, tools, reply check. Shared by real
/// conversations and the dashboard's test chat, so both behave exactly alike.
/// </summary>
internal sealed class AgentBrain(ILlmProvider llm, AgentTools tools, IOptions<AiOptions> options)
{
    private AiOptions Options => options.Value;

    public async Task<Thought> ThinkAsync(string systemPrompt, IReadOnlyList<AiHistoryMessage> history, CancellationToken ct)
    {
        var messages = new List<LlmMessage> { LlmMessage.System(systemPrompt) };
        messages.AddRange(history.Select(m => m.Author == "customer" ? LlmMessage.User(m.Text) : LlmMessage.Assistant(m.Text)));
        var usedTools = new List<string>();
        int input = 0, output = 0;
        Thought Done(string? reply = null, string? handoff = null, string? question = null, string? blocked = null, string? failed = null) =>
            new(reply, handoff, question, blocked, failed, input, output, usedTools, [.. tools.Sources]);

        for (var round = 0; round <= Options.MaxToolRounds; round++)
        {
            LlmResponse response;
            try
            {
                response = await llm.CompleteAsync(
                    new LlmRequest(Options.ReplyModel, messages, round < Options.MaxToolRounds ? AgentTools.Definitions : [], Options.MaxOutputTokens),
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
                return Guardrails.Check(reply, tools.AllowedPrices) is { } blocked ? Done(blocked: blocked) : Done(reply: reply);
            }

            messages.Add(new LlmMessage(LlmRole.Assistant, response.Text, response.ToolCalls));
            foreach (var call in response.ToolCalls)
            {
                usedTools.Add(call.Name.Length <= 40 ? call.Name : call.Name[..40]);
                var outcome = await tools.RunAsync(call, ct);
                if (outcome.HandoffReason is { } reason)
                {
                    return Done(handoff: reason, question: outcome.Question);
                }

                messages.Add(LlmMessage.ToolResult(call.Id, outcome.Json));
            }
        }

        return Done(blocked: "too_many_tool_rounds");
    }
}

/// <summary>The workspace's AI settings and shop name, with defaults until an owner saves.</summary>
internal sealed class AiSettingsStore(AiDbContext db, ITenantContext tenant, IWorkspaceInfo workspace)
{
    public async Task<AiSettings> GetAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? AiSettings.Defaults(tenant.TenantId);

    public async Task<string> ShopNameAsync(CancellationToken ct) => await workspace.NameAsync(ct) ?? "the shop";
}
