using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.Services.Identity.Api.Tokens;

// The key Identity signs every token with (ES256), and its public half for the key set. The key id is the thumbprint
// of the public key (RFC 7638), so the same key keeps the same id across restarts and deploys.
internal sealed class SigningKey : IDisposable
{
    private readonly ECDsa key;

    public SigningKey(IOptions<TokenOptions> options)
    {
        if (!TryLoad(options.Value.SigningKey, out var loaded))
        {
            throw new InvalidOperationException("Tokens:SigningKey must be a P-256 private key, PKCS#8 in Base64.");
        }

        key = loaded;
        var q = key.ExportParameters(includePrivateParameters: false).Q;
        var x = Base64UrlEncoder.Encode(q.X);
        var y = Base64UrlEncoder.Encode(q.Y);
        var id = Base64UrlEncoder.Encode(new JsonWebKey { Kty = "EC", Crv = "P-256", X = x, Y = y }.ComputeJwkThumbprint());

        PublicKey = new PublicKey("EC", "P-256", x, y, id, "sig", SecurityAlgorithms.EcdsaSha256);
        Credentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = id }, SecurityAlgorithms.EcdsaSha256);
    }

    public PublicKey PublicKey { get; }

    public SigningCredentials Credentials { get; }

    public static bool TryLoad(string base64, [NotNullWhen(true)] out ECDsa? key)
    {
        key = null;
        var bytes = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, bytes, out var length))
        {
            return false;
        }

        var candidate = ECDsa.Create();
        try
        {
            candidate.ImportPkcs8PrivateKey(bytes.AsSpan(0, length), out _);
            if (IsP256(candidate.ExportParameters(includePrivateParameters: false).Curve))
            {
                key = candidate;
                return true;
            }
        }
        catch (CryptographicException)
        {
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }

        candidate.Dispose();
        return false;
    }

    public void Dispose() => key.Dispose();

    // Windows names the curve without an OID value, Linux with one.
    private static bool IsP256(ECCurve curve) =>
        curve.IsNamed
        && (curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value
            || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256");
}

// One entry of the key set (RFC 7517), the public half only.
internal sealed record PublicKey(string Kty, string Crv, string X, string Y, string Kid, string Use, string Alg);