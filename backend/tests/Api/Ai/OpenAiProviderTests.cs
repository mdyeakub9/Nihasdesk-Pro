using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>What we send to OpenAI and how we read its answers (ADR 0006), against canned responses.</summary>
public sealed class OpenAiProviderTests
{
    private sealed class Canned(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Sent { get; private set; }

        public string? Authorization { get; private set; }

        public Uri? Url { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (OpenAiProvider Provider, Canned Handler) Create(HttpStatusCode status, string body, string? effort = "low")
    {
        var handler = new Canned(status, body);
        var options = new AiOptions { ReasoningEffort = effort, OpenAi = { ApiKey = "sk-test-only" } };
        var http = new HttpClient(handler) { BaseAddress = new Uri(options.OpenAi.BaseUrl) };
        return (new OpenAiProvider(http, Options.Create(options)), handler);
    }

    private static LlmRequest Request(int maxOutputTokens = 2000) =>
        new("gpt-5-mini", [LlmMessage.System("rules"), LlmMessage.User("Avete la borsa nera?")], AgentTools.Definitions, maxOutputTokens);

    [Fact]
    public async Task Sends_model_tools_reasoning_effort_and_room_for_the_answer_to_the_eu_endpoint()
    {
        var (provider, handler) = Create(HttpStatusCode.OK, """
            {"choices":[{"message":{"content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"search_products","arguments":"{\"query\":\"borsa\"}"}}]},"finish_reason":"tool_calls"}],
             "usage":{"prompt_tokens":120,"completion_tokens":30}}
            """);

        var response = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("https://eu.api.openai.com/v1/chat/completions", handler.Url!.ToString());
        Assert.Equal("Bearer sk-test-only", handler.Authorization);
        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal("low", sent.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.Equal(2000, sent.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("search_products", sent.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("system", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());

        var call = Assert.Single(response.ToolCalls);
        Assert.Equal(("call_1", "search_products", """{"query":"borsa"}"""), (call.Id, call.Name, call.Arguments));
        Assert.Equal(new LlmUsage(120, 30), response.Usage);
    }

    [Fact]
    public async Task Leaves_reasoning_effort_out_for_models_without_it()
    {
        var (provider, handler) = Create(HttpStatusCode.OK, """{"choices":[{"message":{"content":"Ciao!"},"finish_reason":"stop"}]}""", effort: null);

        var response = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("Ciao!", response.Text);
        Assert.DoesNotContain("reasoning_effort", handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Names_an_exhausted_output_budget_and_rejected_keys()
    {
        var (exhausted, _) = Create(HttpStatusCode.OK, """{"choices":[{"message":{"content":""},"finish_reason":"length"}],"usage":{"prompt_tokens":1900,"completion_tokens":600}}""");
        Assert.Equal("output_limit", (await Assert.ThrowsAsync<LlmException>(() => exhausted.CompleteAsync(Request(600), CancellationToken.None))).Reason);

        var (rejected, _) = Create(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key"}}""");
        Assert.Equal("http_401", (await Assert.ThrowsAsync<LlmException>(() => rejected.CompleteAsync(Request(), CancellationToken.None))).Reason);
    }
}
