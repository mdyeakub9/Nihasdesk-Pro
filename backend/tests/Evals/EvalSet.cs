using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nihasdesk.Evals;

/// <summary>One chat message of a case; the last one is always the customer's.</summary>
internal sealed record EvalMessage(string Author, string Text);

/// <summary>
/// What a good outcome is. <see cref="Handoff"/>: the AI must hand over to the team (unknown,
/// complaint, wants a person) instead of answering. <see cref="Answer"/>: what a correct reply says,
/// for the judge; not an exact text. <see cref="DocumentPhrase"/>: the words the right section of the
/// shop's document contains, to measure search (recall@4).
/// </summary>
internal sealed record EvalExpectation(bool Handoff, string? Answer, string? DocumentPhrase = null);

internal sealed record EvalCase(string Id, string Topic, string Language, IReadOnlyList<EvalMessage> Messages, EvalExpectation Expect);

internal sealed record EvalSetFile(int Version, IReadOnlyList<EvalCase> Cases);

internal static class EvalFiles
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static EvalSetFile LoadSet(string path)
    {
        var set = JsonSerializer.Deserialize<EvalSetFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{path} is empty.");
        var problems = new List<string>();
        foreach (var c in set.Cases)
        {
            if (c.Messages.Count == 0 || c.Messages[^1].Author != "customer")
            {
                problems.Add($"{c.Id}: the last message must be the customer's.");
            }

            if (c.Messages.Any(m => m.Author is not ("customer" or "ai") || string.IsNullOrWhiteSpace(m.Text)))
            {
                problems.Add($"{c.Id}: authors are customer or ai, texts are not empty.");
            }

            if (!c.Expect.Handoff && string.IsNullOrWhiteSpace(c.Expect.Answer))
            {
                problems.Add($"{c.Id}: a case that expects an answer says what a correct answer is.");
            }
        }

        foreach (var duplicate in set.Cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"{duplicate.Key}: the id is used twice.");
        }

        return problems.Count == 0 ? set : throw new InvalidDataException(string.Join(Environment.NewLine, problems));
    }
}
