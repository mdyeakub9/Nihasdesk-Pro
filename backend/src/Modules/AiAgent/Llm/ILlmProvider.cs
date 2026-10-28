using System.Text.Json;

namespace Nihasdesk.Modules.AiAgent.Llm;

internal enum LlmRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>A tool call the model asks for; <see cref="Arguments"/> is the model's JSON, untrusted.</summary>
internal sealed record LlmToolCall(string Id, string Name, string Arguments);

internal sealed record LlmMessage(LlmRole Role, string? Content, IReadOnlyList<LlmToolCall>? ToolCalls = null, string? ToolCallId = null)
{
    public static LlmMessage System(string text) => new(LlmRole.System, text);

    public static LlmMessage User(string text) => new(LlmRole.User, text);

    public static LlmMessage Assistant(string text) => new(LlmRole.Assistant, text);

    public static LlmMessage ToolResult(string callId, string json) => new(LlmRole.Tool, json, ToolCallId: callId);
}

/// <summary>A tool the model may call: name, description and a JSON schema for the arguments.</summary>
internal sealed record LlmTool(string Name, string Description, JsonElement Parameters);

internal sealed record LlmRequest(string Model, IReadOnlyList<LlmMessage> Messages, IReadOnlyList<LlmTool> Tools, int MaxOutputTokens);

internal sealed record LlmUsage(int InputTokens, int OutputTokens);

internal sealed record LlmResponse(string? Text, IReadOnlyList<LlmToolCall> ToolCalls, LlmUsage Usage);

/// <summary>Any model provider (ADR 0006): the agent logic never knows which one answers.</summary>
internal interface ILlmProvider
{
    /// <summary>False when no API key is configured: the AI then stays off.</summary>
    bool IsConfigured { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

/// <summary>The provider failed (network, rate limit, refusal); the conversation goes to the team.</summary>
internal sealed class LlmException(string reason) : Exception($"LLM call failed: {reason}")
{
    public string Reason { get; } = reason;
}
