using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.Services.Identity.Api.Tokens;

// Signs the technical tokens. There is no user behind them, so the token names the client as its subject
// (RFC 9068, section 2.2). A real login later issues tokens of the same shape with the user as subject.
internal sealed class TokenIssuer(IOptions<TokenOptions> options, SigningKey key, TimeProvider time)
{
    private readonly JsonWebTokenHandler handler = new();

    public TimeSpan Lifetime => options.Value.Lifetime;

    // Compares hashes of equal length in constant time, so the answer time tells nothing about the secret.
    public bool IsClient(string clientId, string secret) =>
        options.Value.Clients.TryGetValue(clientId, out var client)
        && CryptographicOperations.FixedTimeEquals(Hash(secret), Hash(client.Secret));

    public string Issue(string clientId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = "at+jwt",
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + options.Value.Lifetime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = clientId,
                ["client_id"] = clientId,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = key.Credentials,
        });
    }

    private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}