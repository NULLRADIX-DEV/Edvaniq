using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.Testing;

// Issues tokens like the real issuer does, but signed with a key that exists only for this test run.
public static class TestTokens
{
    public const string Issuer = "edvaniq-tests";

    // The audience every service accepts (appsettings.json of the service).
    public const string Audience = "edvaniq-api";

    public static SymmetricSecurityKey SigningKey { get; } = NewKey();

    public static SymmetricSecurityKey NewKey() => new(RandomNumberGenerator.GetBytes(32));

    // The audience of a service's own token for another service's internal endpoint.
    public const string InternalAudience = "edvaniq-internal";

    // A client's own token, the way Identity issues it: the client is the subject and names itself in client_id.
    public static string CreateForClient(string clientId, string audience = InternalAudience) =>
        Create(clientId, audience: audience, claims: [new Claim("client_id", clientId)]);

    // A token for userId. Without userId it has no subject. An expiry in the past gives an expired token.
    public static string Create(
        string? userId,
        DateTime? expires = null,
        string issuer = Issuer,
        string audience = Audience,
        SecurityKey? signingKey = null,
        IReadOnlyList<Claim>? claims = null)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        IEnumerable<Claim> subject = userId is null ? [] : [new Claim("sub", userId)];

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(subject.Concat(claims ?? [])),
            IssuedAt = expiresAt.AddMinutes(-10),
            NotBefore = expiresAt.AddMinutes(-10),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}