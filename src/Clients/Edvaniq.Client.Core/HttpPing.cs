using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Sends the ping to the web host. Without a session the host answers 401: the ping then starts a guest session and tries
// once more. The HttpClient carries the base address of the host and has to keep the session cookie, as the browser and
// the default HttpClientHandler do. Its timeout has to be longer than the host's own waiting for a token and the gateway,
// so the host's error answer gets through instead of a timeout here.
public sealed class HttpPing(HttpClient http) : IPing
{
    public const string Path = "api/planning/ping";
    public const string SessionPath = "session";

    public async Task<PingResult?> SendAsync(CancellationToken cancellationToken = default)
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
                return null;
            }

            using var second = await http.PostAsync(Path, content: null, cancellationToken);
            return await ReadAsync(second, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<PingResult?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync(PingJson.Default.PingResult, cancellationToken)
            : null;
}

// Source-generated, so trimming the browser app cannot cut away what the serializer needs. An answer without id or time
// fails instead of turning into ping 0 from the year 1.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PingResult))]
internal sealed partial class PingJson : JsonSerializerContext;