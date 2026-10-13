using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

namespace Edvaniq.Web;

// Fetches the web host's technical token from Identity (OAuth 2.0 client credentials) and keeps it in memory. A new
// one is fetched a minute before the old one expires, or after half its lifetime if it lives shorter than 2 minutes.
// Calls that arrive while a fetch runs wait for that fetch and share its result, a failure included. A caller that
// gives up does not cancel the fetch for the others. A fetch gets 7 seconds in all, retries of the standard resilience
// handler included, so a call gets its answer within the 9 seconds the web host gives the gateway (Program.cs).
internal sealed class BackendTokens(
    IHttpClientFactory httpClientFactory,
    IOptions<BackendTokenOptions> options,
    TimeProvider time,
    IHostApplicationLifetime application)
{
    private static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(7);

    private readonly Lock gate = new();
    private volatile CachedToken? current;
    private Task<CachedToken>? fetching;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        if (Valid(current) is { } token)
        {
            return token;
        }

        Task<CachedToken> fetch;
        lock (gate)
        {
            if (Valid(current) is { } fetchedMeanwhile)
            {
                return fetchedMeanwhile;
            }

            // A finished fetch is never reused: its token is the one that just ran out, or it failed.
            if (fetching is null || fetching.IsCompleted)
            {
                fetching = FetchAsync();
            }

            fetch = fetching;
        }

        return (await fetch.WaitAsync(cancellationToken)).Value;
    }

    private string? Valid(CachedToken? token) =>
        token is not null && time.GetUtcNow() < token.RenewAt ? token.Value : null;

    private async Task<CachedToken> FetchAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(application.ApplicationStopping);
        timeout.CancelAfter(FetchTimeout);
        var cancellationToken = timeout.Token;

        // Taken before the request, so the time the request takes counts against the token.
        var requestedAt = time.GetUtcNow();
        var settings = options.Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
        });

        using var client = httpClientFactory.CreateClient(nameof(BackendTokens));
        using var response = await client.PostAsync(settings.TokenEndpoint, form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var answer = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (string.IsNullOrEmpty(answer?.AccessToken) || answer.ExpiresIn <= 0)
        {
            throw new InvalidOperationException("Identity answered without a usable token.");
        }

        var lifetime = TimeSpan.FromSeconds(answer.ExpiresIn);
        var renewAfter = lifetime > 2 * RenewBeforeExpiry ? lifetime - RenewBeforeExpiry : lifetime / 2;
        var token = new CachedToken(answer.AccessToken, requestedAt + renewAfter);
        current = token;
        return token;
    }

    private sealed record CachedToken(string Value, DateTimeOffset RenewAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}