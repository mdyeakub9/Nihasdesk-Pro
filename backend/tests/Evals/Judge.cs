using System.Text.Json;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Evals;

/// <summary>The judge's view of one outcome: <c>correct</c>, <c>acceptable</c> or <c>wrong</c>.</summary>
internal sealed record Verdict(string Grade, bool MadeUpFact, string Reason);

/// <summary>
/// A stronger model grades each outcome against the case's expectation and the shop's facts (ADR
/// 0026 §6). It only reads; it calls no tools and its words never reach a customer.
/// </summary>
internal sealed class Judge(ILlmProvider llm, string model)
{
    private const string System = """
        You grade the replies of a shop's AI assistant in a test. You get the shop's complete data (JSON),
        the conversation, what a correct outcome is, and what the assistant did.

        Grades:
        - "correct": does what the expectation says, every fact matches the shop data.
        - "acceptable": no wrong facts and helpful, but misses part of the expectation, or asks a reasonable clarifying question instead.
        - "wrong": states a fact the shop data contradicts or doesn't contain, answers when it should hand over, hands over when the shop data had the answer, or doesn't help.

        "madeUpFact" is true when the reply states a price, stock level, delivery time, policy, contact detail or product
        that is not in the shop data (rounding or a different currency format of a real amount is not made up).
        A handover to the team states no facts.

        Answer with JSON only: {"grade":"correct|acceptable|wrong","madeUpFact":true|false,"reason":"one short sentence"}
        """;

    public async Task<Verdict> GradeAsync(EvalCase evalCase, string shopJson, string outcome, CancellationToken ct)
    {
        var conversation = string.Join("\n", evalCase.Messages.Select(m => $"{m.Author}: {m.Text}"));
        var expectation = evalCase.Expect.Handoff
            ? "The assistant must hand the conversation to the team instead of answering." + (evalCase.Expect.Answer is { } note ? $" ({note})" : "")
            : $"The assistant answers. A correct answer: {evalCase.Expect.Answer}";
        var user = $"""
            Shop data:
            {shopJson}

            Conversation:
            {conversation}

            Expectation: {expectation}

            What the assistant did:
            {outcome}
            """;

        LlmResponse response;
        try
        {
            response = await llm.CompleteAsync(new LlmRequest(model, [LlmMessage.System(System), LlmMessage.User(user)], [], 2000), ct);
        }
        catch (LlmException ex)
        {
            return new Verdict("error", false, $"judge failed: {ex.Reason}");
        }

        return Parse(response.Text);
    }

    internal static Verdict Parse(string? text)
    {
        var json = text?.Trim() ?? string.Empty;
        var start = json.IndexOf('{', StringComparison.Ordinal);
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return new Verdict("error", false, "judge gave no JSON");
        }

        try
        {
            using var document = JsonDocument.Parse(json[start..(end + 1)]);
            var root = document.RootElement;
            var grade = root.TryGetProperty("grade", out var g) ? g.GetString() : null;
            return new Verdict(
                grade is "correct" or "acceptable" or "wrong" ? grade : "error",
                root.TryGetProperty("madeUpFact", out var m) && m.ValueKind == JsonValueKind.True,
                root.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty);
        }
        catch (JsonException)
        {
            return new Verdict("error", false, "judge gave invalid JSON");
        }
    }
}
