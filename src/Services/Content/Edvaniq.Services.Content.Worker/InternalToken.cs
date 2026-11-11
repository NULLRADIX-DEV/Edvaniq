using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Worker;

// The worker's own token for Knowledge's internal endpoint (docs/tokens.md): client credentials of the client content
// at Identity. Its audience is only for internal endpoints, no other endpoint of any service takes it. Kept in memory
// and fetched again a minute before it expires. Calls that come while a fetch runs wait for that fetch.
internal sealed class InternalToken(IHttpClientFactory httpClientFactory, IOptions<InternalTokenOptions> options, TimeProvider time)
{
    public const string ClientName = "identity";

    public static readonly Uri IdentityAddress = new("https+http://identity-api");

    private static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim gate = new(1, 1);
    private (string Value, DateTimeOffset RenewAt)? current;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (current is { } token && time.GetUtcNow() < token.RenewAt)
            {
                return token.Value;
            }

            var requestedAt = time.GetUtcNow();
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = InternalTokenOptions.ClientId,
                ["client_secret"] = options.Value.ClientSecret,
            });
            using var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.PostAsync(new Uri("connect/token", UriKind.Relative), form, cancellationToken);
            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (string.IsNullOrEmpty(answer?.AccessToken) || answer.ExpiresIn <= 0)
            {
                throw new InvalidOperationException("Identity answered without a usable token.");
            }

            var lifetime = TimeSpan.FromSeconds(answer.ExpiresIn);
            var renewAfter = lifetime > 2 * RenewBeforeExpiry ? lifetime - RenewBeforeExpiry : lifetime / 2;
            current = (answer.AccessToken, requestedAt + renewAfter);
            return answer.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

// Puts the worker's own token on every request of the client it's added to, only the Knowledge client.
internal sealed class InternalTokenHandler(InternalToken token) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token.GetAsync(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}

// The section InternalToken. The secret comes only from the environment, the start fails without it.
internal sealed class InternalTokenOptions
{
    public const string Section = "InternalToken";

    public const string ClientId = "content";

    public string ClientSecret { get; set; } = "";
}