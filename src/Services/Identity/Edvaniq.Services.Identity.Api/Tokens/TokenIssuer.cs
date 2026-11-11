using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.Services.Identity.Api.Tokens;

// Signs all tokens (docs/tokens.md). A client's own token names the client as its subject, there is no user behind it
// (RFC 9068, section 2.2). A login names the account. Both have the same shape, so a service tells them apart only by
// the subject.
internal sealed class TokenIssuer(IOptions<TokenOptions> options, SigningKey key, TimeProvider time)
{
    private readonly JsonWebTokenHandler handler = new();

    public TimeSpan Lifetime => options.Value.Lifetime;

    public TimeSpan UserLifetime => options.Value.UserLifetime;

    // The client, or null for an unknown one or a wrong secret. Compares hashes of equal length in constant time, so
    // the answer time tells nothing about the secret.
    public TokenClient? FindClient(string clientId, string secret) =>
        options.Value.Clients.TryGetValue(clientId, out var client)
        && CryptographicOperations.FixedTimeEquals(Hash(secret), Hash(client.Secret))
            ? client
            : null;

    // A client with its own audience gets it, a service's internal client gets one the services turn away.
    public string Issue(string clientId, TokenClient client) =>
        Issue(clientId, clientId, Lifetime, string.IsNullOrEmpty(client.Audience) ? options.Value.Audience : client.Audience);

    // A login is always for the services.
    public string IssueForAccount(Guid accountId, string clientId) =>
        Issue(accountId.ToString("D"), clientId, UserLifetime, options.Value.Audience);

    private string Issue(string subject, string clientId, TimeSpan lifetime, string audience)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = "at+jwt",
            Issuer = options.Value.Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifetime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["client_id"] = clientId,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = key.Credentials,
        });
    }

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}