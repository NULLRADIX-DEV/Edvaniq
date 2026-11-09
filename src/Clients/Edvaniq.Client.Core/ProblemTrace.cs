using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// The trace id of a problem the host answered with. Only the id as ServiceDefaults writes it counts: 32 hex digits.
// Anything else in an error answer says nothing a user could pass on.
internal sealed record ProblemTrace(string? TraceId = null)
{
    public static async Task<string?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync(ProblemJson.Default.ProblemTrace, cancellationToken);
            return problem?.TraceId is { Length: 32 } id && id.All(char.IsAsciiHexDigitLower) ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ProblemTrace))]
internal sealed partial class ProblemJson : JsonSerializerContext;