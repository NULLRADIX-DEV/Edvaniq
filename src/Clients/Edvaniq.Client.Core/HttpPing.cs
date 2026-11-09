using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Sends the ping to the web host. The ping wants a login: without one the host answers 401 and the ping says the user
// is signed out. The HttpClient carries the base address of the host and has to keep the session cookie, as the browser
// does. Its timeout has to be longer than the host's own waiting for a token and the gateway, so the host's error answer
// gets through instead of a timeout here. A failed ping carries the trace id of the problem the host answered with, if
// there was one.
public sealed class HttpPing(HttpClient http) : IPing
{
    public const string Path = "api/planning/ping";

    public async Task<PingOutcome> SendAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsync(Path, content: null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return PingOutcome.SignedOut;
            }

            return response.IsSuccessStatusCode
                ? new PingOutcome(await response.Content.ReadFromJsonAsync(PingJson.Default.PingResult, cancellationToken))
                : PingOutcome.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
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
}

// Source-generated, so trimming the browser app cannot cut away what the serializer needs. An answer without id or time
// fails instead of turning into ping 0 from the year 1.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PingResult))]
internal sealed partial class PingJson : JsonSerializerContext;