using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Evals;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.TestSupport;

// The AI eval runner (PRD 4.8, ADR 0026 §6). Usage and options: docs/evals/README.md.
// The AI key comes only from the environment (Ai__OpenAi__ApiKey); it is never printed or saved.

var options = Options.Parse(args);
var root = Options.RepositoryRoot();
var evals = Path.Combine(root, "docs", "evals");
var set = EvalFiles.LoadSet(options.Set ?? Path.Combine(evals, "eval-set.json"));
var shop = TestShop.Load(options.Shop ?? Path.Combine(evals, "test-shop.json"));
var cases = set.Cases.Take(options.Limit ?? int.MaxValue).ToList();
Console.WriteLine($"Eval set v{set.Version}: {cases.Count} cases for {shop.Name}{(options.DryRun ? " (dry run: scripted AI, no judge, nothing saved)" : "")}.");

if (!options.DryRun && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Ai__OpenAi__ApiKey")))
{
    Console.Error.WriteLine("Set Ai__OpenAi__ApiKey (or use --dry-run).");
    return 2;
}

// Run from anywhere in the checkout: the API's content root (appsettings.json) is under backend/src.
Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_NIHASDESK_API", Path.Combine(root, "backend", "src", "Nihasdesk.Api"));
await using var database = await PostgresDatabase.StartAsync();
var settings = new Dictionary<string, string?>
{
    ["Logging:LogLevel:Default"] = "Warning",
    ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
};
await using var api = new NihasdeskApiFactory(database, settings, useRealAi: !options.DryRun);
using var owner = api.CreateApiClient();
(await owner.SignupAndVerifyAsync(api.Emails, "eval-owner@example.com", shop.Name)).EnsureSuccessStatusCode();
await shop.SeedAsync(owner);
var shopPrices = shop.Prices();

var llm = api.Services.GetRequiredService<ILlmProvider>();
var judge = options.DryRun ? null : new Judge(llm, options.JudgeModel);
var results = new List<CaseResult>();
string? replyModel = null, instructionVersion = null;
var started = DateTimeOffset.UtcNow;

for (var i = 0; i < cases.Count; i++)
{
    // The test chat allows 30 turns per hour and workspace (SEC-39): move the test clock on.
    if (i > 0 && i % 25 == 0)
    {
        api.Clock.Advance(TimeSpan.FromMinutes(61));
    }

    var evalCase = cases[i];
    var watch = Stopwatch.StartNew();
    var response = await owner.PostAsync("/api/v1/ai/test", new { messages = evalCase.Messages });
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"{evalCase.Id}: the test chat answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    var turn = (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    watch.Stop();
    var trace = turn.GetProperty("trace");
    replyModel ??= trace.GetProperty("model").GetString();
    instructionVersion ??= trace.GetProperty("instructionVersion").GetString();

    var reply = Text(turn, "reply");
    var handoff = Text(turn, "handoff");
    var blocked = Text(turn, "blocked");
    var failed = Text(turn, "failed");
    var outcome = failed is not null ? "failed" : blocked is not null ? "blocked" : reply is not null ? "reply" : "handoff";

    // Amounts the shop or this turn's tools stated (quotes add up totals); anything else is made up.
    var allowed = new HashSet<long>(shopPrices);
    foreach (var tool in trace.GetProperty("tools").EnumerateArray())
    {
        allowed.UnionWith(Guardrails.Amounts(tool.GetProperty("result").GetString()));
    }

    var madeUp = Guardrails.Amounts(reply).Where(cents => !allowed.Contains(cents)).Distinct().ToList();
    var handoffOk = evalCase.Expect.Handoff ? outcome == "handoff" : outcome == "reply";

    var verdict = judge is null || outcome == "failed"
        ? new Verdict(outcome == "failed" ? "error" : "skipped", false, outcome == "failed" ? "the AI provider failed" : "dry run")
        : await judge.GradeAsync(
            evalCase,
            shop.Json,
            outcome switch
            {
                "reply" => $"Replied to the customer: {reply}",
                "handoff" => $"Handed the conversation to the team (reason: {handoff}).",
                _ => $"Its draft was held back by the reply check ({blocked}); the team answers instead.",
            },
            CancellationToken.None);

    results.Add(new CaseResult(
        evalCase.Id,
        evalCase.Topic,
        evalCase.Language,
        outcome,
        reply,
        handoff,
        blocked,
        failed,
        evalCase.Expect.Handoff,
        handoffOk,
        verdict.Grade,
        verdict.MadeUpFact,
        madeUp,
        verdict.Reason,
        [.. trace.GetProperty("toolNames").EnumerateArray().Select(t => t.GetString()!)],
        trace.GetProperty("inputTokens").GetInt32() + trace.GetProperty("outputTokens").GetInt32(),
        (int)watch.ElapsedMilliseconds,
        verdict.WrongLanguage,
        [.. trace.GetProperty("tools").EnumerateArray().Select(c => new ToolCallResult(
            c.GetProperty("name").GetString()!,
            c.GetProperty("arguments").GetString()!,
            c.GetProperty("result").GetString()!))]));
    Console.WriteLine($"[{i + 1}/{cases.Count}] {evalCase.Id}: {outcome}{(failed is null ? "" : $" ({failed})")}, {verdict.Grade}{(handoffOk ? "" : ", handoff wrong")}{(madeUp.Count > 0 ? ", MADE-UP PRICE" : "")}{(verdict.MadeUpFact ? ", MADE-UP FACT" : "")}");

    // A rejected key or endpoint fails every case: stop instead of saving a run that measures nothing.
    if (i == 2 && results.All(r => r.Outcome == "failed"))
    {
        Console.Error.WriteLine(
            $"The AI provider failed for the first cases ({string.Join(", ", results.Select(r => r.Failed).Distinct())}). " +
            "http_401/403: the key is wrong or not for this endpoint (Ai__OpenAi__BaseUrl; the default is the EU endpoint). Nothing was saved.");
        return 3;
    }
}

if (!options.DryRun && results.Any(r => r.Outcome == "failed"))
{
    Console.Error.WriteLine($"{results.Count(r => r.Outcome == "failed")} cases failed at the AI provider; nothing was saved. Run again.");
    return 3;
}

var run = new RunFile(started, replyModel ?? "?", judge is null ? "none" : options.JudgeModel, instructionVersion, set.Version, Report.Totals(results), results, Judge.RulesVersion);
var t = run.Totals;
Console.WriteLine($"Correct {t.Correct}, acceptable {t.Acceptable}, wrong {t.Wrong}, handoff right {t.HandoffCorrect}/{t.Cases}, made-up prices {t.MadeUpPricesReached}, made-up facts {t.MadeUpFacts}, wrong language {t.WrongLanguage}.");

if (!options.DryRun)
{
    var runs = Path.Combine(evals, "runs");
    Directory.CreateDirectory(runs);
    var name = string.Create(CultureInfo.InvariantCulture, $"{started:yyyy-MM-dd-HHmm}-{run.ReplyModel}.json");
    var path = Path.Combine(runs, name);
    var previous = Report.Previous(runs, except: path);
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(run, EvalFiles.Json));
    await File.WriteAllTextAsync(Path.Combine(evals, "report.md"), Report.Markdown(run, name, previous));
    Console.WriteLine($"Saved docs/evals/runs/{name} and docs/evals/report.md.");
}

// Made-up prices or facts must be 0 (PRD 4.8): fail, so a prompt or model change can't slip through.
return t.MadeUpPricesReached > 0 || t.MadeUpFacts > 0 ? 1 : 0;

static string? Text(JsonElement turn, string name) =>
    turn.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

internal sealed record Options(string? Set, string? Shop, int? Limit, string JudgeModel, bool DryRun)
{
    public static Options Parse(string[] args)
    {
        string? Value(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        return new Options(
            Value("--set"),
            Value("--shop"),
            Value("--limit") is { } limit ? int.Parse(limit, CultureInfo.InvariantCulture) : null,
            Value("--judge-model") ?? "gpt-5",
            args.Contains("--dry-run"));
    }

    /// <summary>The checkout this runs in: the folder that has <c>docs/evals</c>.</summary>
    public static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "docs", "evals")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Run from inside the repository (docs/evals not found).");
    }
}
