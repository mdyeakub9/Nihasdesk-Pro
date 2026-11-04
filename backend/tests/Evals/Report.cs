using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nihasdesk.Evals;

/// <summary>
/// One case's outcome. <see cref="Outcome"/>: <c>reply</c>, <c>handoff</c>, <c>blocked</c> (the reply
/// check held the draft back) or <c>failed</c> (the provider). <see cref="MadeUpPrices"/>: amounts in
/// the reply that neither the shop nor this turn's tools stated, in cents.
/// </summary>
internal sealed record CaseResult(
    string Id,
    string Topic,
    string Language,
    string Outcome,
    string? Reply,
    string? Handoff,
    string? Blocked,
    string? Failed,
    bool ExpectHandoff,
    bool HandoffOk,
    string Grade,
    bool MadeUpFact,
    IReadOnlyList<long> MadeUpPrices,
    string Reason,
    IReadOnlyList<string> Tools,
    int Tokens,
    int DurationMs);

/// <summary>RecallAt4 stays "n/a" until search returns ranked sections (ADR 0026 §3).</summary>
internal sealed record RunTotals(
    int Cases,
    int Correct,
    int Acceptable,
    int Wrong,
    int JudgeErrors,
    int HandoffCorrect,
    int MadeUpPricesReached,
    int MadeUpFacts,
    int HeldBack,
    int ProviderFailures,
    int AverageTokens,
    double AverageSeconds,
    string RecallAt4);

internal sealed record RunFile(
    DateTimeOffset StartedAt,
    string ReplyModel,
    string JudgeModel,
    string? InstructionVersion,
    int SetVersion,
    RunTotals Totals,
    IReadOnlyList<CaseResult> Cases);

internal static class Report
{
    public static RunTotals Totals(IReadOnlyList<CaseResult> cases) => new(
        cases.Count,
        cases.Count(c => c.Grade == "correct"),
        cases.Count(c => c.Grade == "acceptable"),
        cases.Count(c => c.Grade == "wrong"),
        cases.Count(c => c.Grade == "error"),
        cases.Count(c => c.HandoffOk),
        cases.Count(c => c.MadeUpPrices.Count > 0),
        cases.Count(c => c.MadeUpFact),
        cases.Count(c => c.Outcome == "blocked"),
        cases.Count(c => c.Outcome == "failed"),
        cases.Count == 0 ? 0 : (int)cases.Average(c => c.Tokens),
        cases.Count == 0 ? 0 : Math.Round(cases.Average(c => c.DurationMs) / 1000.0, 1),
        "n/a");

    /// <summary>The newest earlier run in <paramref name="runsDirectory"/>, to compare with.</summary>
    public static RunFile? Previous(string runsDirectory, string? except)
    {
        var file = Directory.Exists(runsDirectory)
            ? Directory.GetFiles(runsDirectory, "*.json").Where(f => f != except).Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        return file is null ? null : JsonSerializer.Deserialize<RunFile>(File.ReadAllText(file), EvalFiles.Json);
    }

    public static string Markdown(RunFile run, string runFileName, RunFile? previous)
    {
        var t = run.Totals;
        var p = previous?.Totals;
        var md = new StringBuilder();
        md.AppendLine("# AI eval report");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Latest run: `runs/{runFileName}` · {run.StartedAt:yyyy-MM-dd HH:mm} UTC · reply model `{run.ReplyModel}` · judge `{run.JudgeModel}` · prompt `{run.InstructionVersion ?? "?"}` · eval set v{run.SetVersion}");
        md.AppendLine();
        md.AppendLine(previous is null
            ? "No earlier run to compare with: this is the baseline."
            : string.Create(CultureInfo.InvariantCulture, $"Compared with the run of {previous.StartedAt:yyyy-MM-dd HH:mm} UTC (reply model `{previous.ReplyModel}`, prompt `{previous.InstructionVersion ?? "?"}`)."));
        md.AppendLine();
        md.AppendLine("| Measure | This run | Before | Change |");
        md.AppendLine("|---|---|---|---|");
        Row(md, "Correct", t.Correct, p?.Correct, t.Cases);
        Row(md, "Acceptable", t.Acceptable, p?.Acceptable, t.Cases);
        Row(md, "Wrong", t.Wrong, p?.Wrong, t.Cases);
        Row(md, "Handoff right (should / shouldn't)", t.HandoffCorrect, p?.HandoffCorrect, t.Cases);
        Row(md, "**Made-up prices reaching the customer (must be 0)**", t.MadeUpPricesReached, p?.MadeUpPricesReached, null);
        Row(md, "**Made-up facts per the judge (must be 0)**", t.MadeUpFacts, p?.MadeUpFacts, null);
        Row(md, "Drafts held back by the reply check", t.HeldBack, p?.HeldBack, null);
        Row(md, "Provider failures", t.ProviderFailures, p?.ProviderFailures, null);
        Row(md, "Judge errors", t.JudgeErrors, p?.JudgeErrors, null);
        md.AppendLine(CultureInfo.InvariantCulture, $"| Average tokens per turn | {t.AverageTokens} | {p?.AverageTokens.ToString(CultureInfo.InvariantCulture) ?? "–"} | {Delta(t.AverageTokens, p?.AverageTokens)} |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Average seconds per turn | {t.AverageSeconds:0.0} | {(p is null ? "–" : p.AverageSeconds.ToString("0.0", CultureInfo.InvariantCulture))} | |");
        md.AppendLine(CultureInfo.InvariantCulture, $"| Retrieval recall@4 | {t.RecallAt4} | {p?.RecallAt4 ?? "–"} | comes with hybrid search (Issue #53) |");
        md.AppendLine();

        md.AppendLine("## By topic");
        md.AppendLine();
        md.AppendLine("| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var topic in run.Cases.GroupBy(c => c.Topic).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {topic.Key} | {topic.Count()} | {topic.Count(c => c.Grade == "correct")} | {topic.Count(c => c.Grade == "acceptable")} | {topic.Count(c => c.Grade == "wrong")} | {topic.Count(c => c.HandoffOk)} |");
        }

        md.AppendLine();
        if (previous is not null)
        {
            var before = previous.Cases.ToDictionary(c => c.Id);
            var changed = run.Cases
                .Where(c => before.TryGetValue(c.Id, out var b) && (b.Grade != c.Grade || b.HandoffOk != c.HandoffOk))
                .ToList();
            md.AppendLine("## Changed since the last run");
            md.AppendLine();
            if (changed.Count == 0)
            {
                md.AppendLine("No case changed its grade or handoff.");
            }

            foreach (var c in changed)
            {
                var b = before[c.Id];
                md.AppendLine(CultureInfo.InvariantCulture, $"- `{c.Id}`: {b.Grade} → **{c.Grade}**{(b.HandoffOk != c.HandoffOk ? $", handoff {(c.HandoffOk ? "now right" : "now wrong")}" : "")} — {c.Reason}");
            }

            var added = run.Cases.Where(c => !before.ContainsKey(c.Id)).Select(c => $"`{c.Id}`").ToList();
            if (added.Count > 0)
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- New cases: {string.Join(", ", added)}");
            }

            md.AppendLine();
        }

        md.AppendLine("## To look at");
        md.AppendLine();
        var look = run.Cases.Where(c => c.Grade is "wrong" or "error" || !c.HandoffOk || c.MadeUpFact || c.MadeUpPrices.Count > 0).ToList();
        if (look.Count == 0)
        {
            md.AppendLine("Nothing: every case was correct or acceptable with the right handoff.");
        }

        foreach (var c in look)
        {
            var what = c.Outcome switch
            {
                "reply" => $"replied “{Shorten(c.Reply)}”",
                "handoff" => $"handed over ({c.Handoff})",
                "blocked" => $"draft held back ({c.Blocked})",
                _ => $"provider failed ({c.Failed})",
            };
            var flags = new List<string>();
            if (!c.HandoffOk)
            {
                flags.Add(c.ExpectHandoff ? "should have handed over" : "should have answered");
            }

            if (c.MadeUpFact)
            {
                flags.Add("made-up fact");
            }

            if (c.MadeUpPrices.Count > 0)
            {
                flags.Add("made-up price " + string.Join(", ", c.MadeUpPrices.Select(x => (x / 100m).ToString("0.00", CultureInfo.InvariantCulture))));
            }

            md.AppendLine(CultureInfo.InvariantCulture, $"- `{c.Id}` ({c.Language}, {c.Grade}{(flags.Count > 0 ? "; " + string.Join("; ", flags) : "")}): {what}. {c.Reason}");
        }

        md.AppendLine();
        md.AppendLine("How to run, read and extend the eval set: [README.md](README.md).");
        return md.ToString();
    }

    private static void Row(StringBuilder md, string name, int now, int? before, int? of) =>
        md.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {now}{(of is { } total ? $" / {total}" : "")} | {before?.ToString(CultureInfo.InvariantCulture) ?? "–"} | {Delta(now, before)} |");

    private static string Delta(int now, int? before) =>
        before is { } b ? (now - b).ToString("+0;-0;0", CultureInfo.InvariantCulture) : "";

    private static string Shorten(string? text) =>
        text is null ? string.Empty : (text.Length <= 140 ? text : text[..140] + "…").ReplaceLineEndings(" ");
}
