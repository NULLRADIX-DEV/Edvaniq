using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Configuration;

namespace Edvaniq.BuildingBlocks.AI;

// Where a provider with an OpenAI-compatible API lives, which model it runs and what goes into every request on top.
// All of it comes from the environment of the server, the repo only knows the names. Errors name the setting, never
// its value.
internal sealed record OpenAiCompatibleSettings(Uri Endpoint, string Model, string ApiKey, JsonObject ExtraBody, bool SchemaOutput = true)
{
    public const string Section = "Ai:OpenAiCompatible";

    // Fields the client sets itself or that never leave the server.
    internal static readonly string[] ReservedFields = ["model", "messages", "stream", "user"];

    public static OpenAiCompatibleSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var endpoint = Required(section, "Endpoint");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{Section}:Endpoint must be an absolute https address.");
        }

        // Relative paths resolve below the endpoint only with a trailing slash.
        if (!uri.AbsolutePath.EndsWith('/'))
        {
            uri = new Uri(uri.AbsoluteUri + "/");
        }

        return new OpenAiCompatibleSettings(
            uri, Required(section, "Model"), Required(section, "ApiKey"), ExtraBodyOf(section["ExtraBody"]), SchemaOutputOf(section["SchemaOutput"]));
    }

    private static string Required(IConfigurationSection section, string key) =>
        section[key] is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"{Section}:{key} is missing.");

    // On unless set to false: an endpoint that can't keep to a JSON schema would otherwise fail every request.
    private static bool SchemaOutputOf(string? value) =>
        string.IsNullOrWhiteSpace(value) ? true
        : bool.TryParse(value.Trim(), out var on) ? on
        : throw new InvalidOperationException($"{Section}:SchemaOutput must be true or false.");

    private static JsonObject ExtraBodyOf(string? value)
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
            throw new InvalidOperationException($"{Section}:ExtraBody must be a JSON object.");
        }

        var reserved = extra.Select(field => field.Key).Where(key => ReservedFields.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
        if (reserved.Count > 0)
        {
            throw new InvalidOperationException($"{Section}:ExtraBody must not set {string.Join(", ", reserved)}.");
        }

        return extra;
    }
}