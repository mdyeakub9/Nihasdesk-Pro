using System.Collections.Concurrent;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.TestSupport;

/// <summary>What the agent sent to the model in one call (ADR 0019).</summary>
public sealed record FakeAiCall(string System, IReadOnlyList<(string Role, string? Text)> Messages, IReadOnlyList<string> Tools)
{
    /// <summary>Results of the tools the agent ran for this turn so far, in order.</summary>
    public IReadOnlyList<string> ToolResults => Messages.Where(m => m.Role == "tool").Select(m => m.Text ?? string.Empty).ToList();
}

/// <summary>A scripted model answer: text, or tool calls (name + JSON arguments).</summary>
public sealed record FakeAiAnswer(string? Text, IReadOnlyList<(string Name, string Arguments)> ToolCalls)
{
    public static FakeAiAnswer Say(string text) => new(text, []);

    public static FakeAiAnswer Call(string name, string arguments) => new(null, [(name, arguments)]);
}

/// <summary>Plays the model for tests: each call is recorded and answered by <see cref="Respond"/>.</summary>
public sealed class FakeAi
{
    public ConcurrentQueue<FakeAiCall> Calls { get; } = new();

    public Func<FakeAiCall, FakeAiAnswer> Respond { get; set; } = _ => FakeAiAnswer.Say("Ciao!");

    /// <summary>When true, every call fails like a provider outage.</summary>
    public bool Fail { get; set; }

    internal ILlmProvider Provider => new Adapter(this);

    private sealed class Adapter(FakeAi fake) : ILlmProvider
    {
        private int _calls;

        public bool IsConfigured => true;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            if (fake.Fail)
            {
                throw new LlmException("http_503");
            }

            var call = new FakeAiCall(
                request.Messages.First(m => m.Role == LlmRole.System).Content ?? string.Empty,
                request.Messages.Where(m => m.Role != LlmRole.System).Select(m => (m.Role.ToString().ToLowerInvariant(), m.Content)).ToList(),
                request.Tools.Select(t => t.Name).ToList());
            fake.Calls.Enqueue(call);
            var answer = fake.Respond(call);
            var id = Interlocked.Increment(ref _calls);
            return Task.FromResult(new LlmResponse(
                answer.Text,
                answer.ToolCalls.Select((c, i) => new LlmToolCall($"call_{id}_{i}", c.Name, c.Arguments)).ToList(),
                new LlmUsage(100, 20)));
        }
    }
}
