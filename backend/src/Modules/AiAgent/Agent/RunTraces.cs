using System.Text.Json;
using System.Text.Json.Nodes;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>
/// Turns a tool call into what "Why this answer" stores (ADR 0026 §6). Tool results are shop data
/// (catalog, zones, policies); the customer's delivery details are never kept (SEC-19, SEC-40).
/// </summary>
internal static class RunTraces
{
    public const int MaxTextLength = 500;
    public const string Passed = "passed";

    /// <summary>Fields of <c>create_order_request</c> that are the customer's personal data.</summary>
    private static readonly string[] PersonalFields = ["customer_name", "phone", "street", "postal_code", "city", "province", "country", "notes"];

    public static ToolTrace Of(LlmToolCall call, ToolOutcome outcome) =>
        new(call.Name.Length <= 40 ? call.Name : call.Name[..40], Arguments(call), Shorten(Result(call, outcome)));

    /// <summary>The profile's orders explain the answer; the customer's name isn't needed for that.</summary>
    private static string Result(LlmToolCall call, ToolOutcome outcome)
    {
        if (call.Name != "get_customer_profile" || JsonNode.Parse(outcome.Json) is not JsonObject result)
        {
            return outcome.Json;
        }

        result.Remove("firstName");
        return result.ToJsonString();
    }

    private static string Arguments(LlmToolCall call)
    {
        if (call.Name != "create_order_request")
        {
            return Shorten(call.Arguments);
        }

        JsonObject? args;
        try
        {
            args = JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments) as JsonObject;
        }
        catch (JsonException)
        {
            return "(not JSON)";
        }

        if (args is null)
        {
            return "(not an object)";
        }

        // Only which details were given, never their values.
        var given = new JsonArray();
        foreach (var field in PersonalFields)
        {
            if (args.Remove(field))
            {
                given.Add(field);
            }
        }

        args["given"] = given;
        return Shorten(args.ToJsonString());
    }

    private static string Shorten(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= MaxTextLength ? text : text[..MaxTextLength] + "…";
}
