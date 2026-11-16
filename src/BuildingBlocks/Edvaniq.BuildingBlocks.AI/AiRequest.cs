using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI;

// What a single request may set beyond the messages (StructuredRequest): its task, so the simulated provider knows
// what is asked, and the extra fields of the body, when a request goes to another model than the configured one and
// needs its own routing. Both travel in ChatOptions.AdditionalProperties, a provider never sends the task.
public static class AiRequest
{
    public const string Task = "edvaniq.task";

    public const string ExtraBody = "edvaniq.extraBody";

    // Fields the client sets itself or that never leave the server.
    internal static readonly string[] ReservedFields = ["model", "messages", "stream", "user"];

    // The task a request was sent for, null if it names none.
    public static string? TaskOf(ChatOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(Task, out var task) == true ? task as string : null;

    internal static JsonObject? ExtraBodyOf(ChatOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(ExtraBody, out var body) == true ? body as JsonObject : null;

    // Extra fields for the body of a request, from a setting with a JSON object. Empty for no value. The error names the
    // setting, never its value.
    public static JsonObject ParseExtraBody(string? value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            node = null;
        }

        if (node is not JsonObject extra)
        {
            throw new InvalidOperationException($"{setting} must be a JSON object.");
        }

        var reserved = extra.Select(field => field.Key).Where(key => ReservedFields.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (reserved.Count > 0)
        {
            throw new InvalidOperationException($"{setting} must not set {string.Join(", ", reserved)}.");
        }

        return extra;
    }
}