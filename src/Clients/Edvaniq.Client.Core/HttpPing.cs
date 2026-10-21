using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Sends the ping to the web host. Without a session the host answers 401: the ping then starts a guest session and tries
// once more. The HttpClient carries the base address of the host and has to keep the session cookie, as the browser and
// the default HttpClientHandler do. Its timeout has to be longer than the host's own waiting for a token and the gateway,
// so the host's error answer gets through instead of a timeout here. A failed ping carries the trace id of the problem
// the host answered with, if there was one.
public sealed class HttpPing(HttpClient http) : IPing
{
    public const string Path = "api/planning/ping";
    public const string SessionPath = "session";

    public async Task<PingOutcome> SendAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var first = await http.PostAsync(Path, content: null, cancellationToken);
            if (first.StatusCode != HttpStatusCode.Unauthorized)
            {
                return await ReadAsync(first, cancellationToken);
            }

            using var session = await http.PostAsync(SessionPath, content: null, cancellationToken);
            if (!session.IsSuccessStatusCode)
            {
                return PingOutcome.Failed(await TraceIdAsync(session, cancellationToken));
            }

            using var second = await http.PostAsync(Path, content: null, cancellationToken);
            return await ReadAsync(second, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return PingOutcome.Failed();
        }
        catch (JsonException)
        {
            return PingOutcome.Failed();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PingOutcome.Failed();
        }
    }

    private static async Task<PingOutcome> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.IsSuccessStatusCode
            ? new PingOutcome(await response.Content.ReadFromJsonAsync(PingJson.Default.PingResult, cancellationToken))
            : PingOutcome.Failed(await TraceIdAsync(response, cancellationToken));

    // Only the id of a problem as ServiceDefaults writes it counts: 32 hex digits. Anything else in an error answer says
    // nothing a user could pass on.
    private static async Task<string?> TraceIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync(PingJson.Default.ProblemTrace, cancellationToken);
            return problem?.TraceId is { Length: 32 } id && id.All(char.IsAsciiHexDigitLower) ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record ProblemTrace(string? TraceId = null);

// Source-generated, so trimming the browser app cannot cut away what the serializer needs. An answer without id or time
// fails instead of turning into ping 0 from the year 1.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PingResult))]
[JsonSerializable(typeof(ProblemTrace))]
internal sealed partial class PingJson : JsonSerializerContext;