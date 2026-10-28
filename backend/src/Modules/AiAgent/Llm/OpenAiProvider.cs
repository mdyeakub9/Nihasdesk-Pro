using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Nihasdesk.Modules.AiAgent.Llm;

/// <summary>
/// OpenAI Chat Completions with function tools (ADR 0006). The default base URL is the EU data
/// residency endpoint. The key lives in user secrets or the secret store, never in the repo or in
/// logs (SEC-13); request logging is off for this client.
/// </summary>
internal sealed class OpenAiProvider(HttpClient http, IOptions<AiOptions> options) : ILlmProvider
{
    public const string HttpClientName = "openai";

    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.OpenAi.ApiKey);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = new JsonArray([.. request.Messages.Select(ToJson)]),
            ["max_completion_tokens"] = request.MaxOutputTokens,
        };
        // Reasoning models (gpt-5 family) count their thinking in the output budget; "low" leaves room
        // for the answer. Empty for models without reasoning, which reject the parameter.
        if (!string.IsNullOrEmpty(options.Value.ReasoningEffort))
        {
            body["reasoning_effort"] = options.Value.ReasoningEffort;
        }

        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray([.. request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.Parameters.GetRawText()),
                },
            })]);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.OpenAi.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new LlmException(ex.GetType().Name);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new LlmException($"http_{(int)response.StatusCode}");
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = json.RootElement;
            var first = root.GetProperty("choices")[0];
            var choice = first.GetProperty("message");
            var text = choice.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null;
            var calls = new List<LlmToolCall>();
            if (choice.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in toolCalls.EnumerateArray())
                {
                    var function = call.GetProperty("function");
                    calls.Add(new LlmToolCall(
                        call.GetProperty("id").GetString() ?? string.Empty,
                        function.GetProperty("name").GetString() ?? string.Empty,
                        function.GetProperty("arguments").GetString() ?? "{}"));
                }
            }

            // Out of output tokens before any answer: say so, rather than "empty reply".
            if (string.IsNullOrWhiteSpace(text) && calls.Count == 0
                && first.TryGetProperty("finish_reason", out var finish) && finish.GetString() == "length")
            {
                throw new LlmException("output_limit");
            }

            var usage = root.TryGetProperty("usage", out var u)
                ? new LlmUsage(u.GetProperty("prompt_tokens").GetInt32(), u.GetProperty("completion_tokens").GetInt32())
                : new LlmUsage(0, 0);
            return new LlmResponse(text, calls, usage);
        }
    }

    private static JsonNode ToJson(LlmMessage m)
    {
        var node = new JsonObject
        {
            ["role"] = m.Role switch
            {
                LlmRole.System => "system",
                LlmRole.User => "user",
                LlmRole.Assistant => "assistant",
                _ => "tool",
            },
            ["content"] = m.Content,
        };
        if (m.ToolCalls is { Count: > 0 } calls)
        {
            node["tool_calls"] = new JsonArray([.. calls.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
            })]);
        }

        if (m.ToolCallId is not null)
        {
            node["tool_call_id"] = m.ToolCallId;
        }

        return node;
    }
}

/// <summary>Section <c>Ai</c>. Model names are configuration, chosen with the eval set (ADR 0006).</summary>
internal sealed class AiOptions
{
    public const string SectionName = "Ai";

    public OpenAiOptions OpenAi { get; set; } = new();

    /// <summary>The model that writes replies.</summary>
    public string ReplyModel { get; set; } = "gpt-5-mini";

    /// <summary>Includes a reasoning model's thinking; the reply itself stays short (prompt, guardrail).</summary>
    public int MaxOutputTokens { get; set; } = 2000;

    /// <summary><c>minimal</c> / <c>low</c> / <c>medium</c> / <c>high</c> for reasoning models; empty for others.</summary>
    public string? ReasoningEffort { get; set; } = "low";

    public int HistorySize { get; set; } = 20;

    /// <summary>Tool rounds per reply before giving up and handing over (SEC-39).</summary>
    public int MaxToolRounds { get; set; } = 4;

    public int MaxRepliesPerConversationPerHour { get; set; } = 10;

    public int MaxRepliesPerWorkspacePerDay { get; set; } = 500;

    /// <summary>Off in tests, which run the processor step by step.</summary>
    public bool BackgroundProcessing { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public sealed class OpenAiOptions
    {
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>EU data residency endpoint by default (ADR 0006).</summary>
        public string BaseUrl { get; set; } = "https://eu.api.openai.com/v1/";
    }
}
