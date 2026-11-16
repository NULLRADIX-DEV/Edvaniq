using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

public sealed class HttpAccountConfirmations(HttpClient http) : IAccountConfirmations
{
    public const string ResendPath = "api/identity/accounts/confirmations/resend";

    public async Task<ResendOutcome> ResendAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                ResendPath, new ResendRequest(email), ConfirmationJson.Default.ResendRequest, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new ResendOutcome(ResendResult.Limited, RetryAfter: response.Headers.RetryAfter?.Delta);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ResendOutcome(ResendResult.Unavailable);
            }

            // The link is on its way either way, a body the client cannot read only loses the note about a delay.
            try
            {
                var answer = await response.Content.ReadFromJsonAsync(ConfirmationJson.Default.ResendAnswer, cancellationToken);
                return new ResendOutcome(ResendResult.Sent, answer?.MailDelayed == true);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return new ResendOutcome(ResendResult.Sent);
            }
        }
        catch (HttpRequestException)
        {
            return new ResendOutcome(ResendResult.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResendOutcome(ResendResult.Unavailable);
        }
    }
}

internal sealed record ResendRequest(string Email);

internal sealed record ResendAnswer(bool MailDelayed);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ResendRequest))]
[JsonSerializable(typeof(ResendAnswer))]
internal sealed partial class ConfirmationJson : JsonSerializerContext;