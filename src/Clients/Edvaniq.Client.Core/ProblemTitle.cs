using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// The title of a problem meant for the user, shown as it is: a limit (429) or a refusal like a full learning goal (409).
// Only a problem counts, with a title that reads like a sentence: not empty, at most MaxLength characters, no control
// characters, and not the bare reason phrase a framework writes by itself. An HTML page from a proxy or the web host's
// own limit have none.
internal sealed record ProblemTitle(string? Title = null)
{
    public const int MaxLength = 300;

    private static readonly string[] ReasonPhrases = ["Too Many Requests", "Conflict"];

    public static async Task<string?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var title = (await response.Content.ReadFromJsonAsync(ProblemTitleJson.Default.ProblemTitle, cancellationToken))?.Title?.Trim();
            return title is { Length: > 0 and <= MaxLength }
                && !title.Any(char.IsControl)
                && !ReasonPhrases.Contains(title, StringComparer.OrdinalIgnoreCase)
                    ? title
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ProblemTitle))]
internal sealed partial class ProblemTitleJson : JsonSerializerContext;