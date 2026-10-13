using System.Security.Cryptography;

using Aspire.Hosting.Publishing;

namespace Edvaniq.AppHost;

// Identity issues the tokens, every service checks them with the public key it fetches from Identity, as on the server
// (deploy/compose.yml). Locally the issuer is a fixed name. Signing key and the web host's client secret are generated
// once and kept in the AppHost's user secrets, like the database passwords.
internal static class TokenIssuerExtensions
{
    public const string Issuer = "edvaniq-local";

    public static IResourceBuilder<ParameterResource> AddSigningKey(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddParameter(name, new SigningKeyDefault(), secret: true, persist: true);

    public static IResourceBuilder<ParameterResource> AddClientSecret(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddParameter(name, new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

    // Identity signs with the key and hands the web host its technical token for the secret.
    public static IResourceBuilder<ProjectResource> WithTokenIssuer(
        this IResourceBuilder<ProjectResource> identity,
        IResourceBuilder<ParameterResource> signingKey,
        IResourceBuilder<ParameterResource> webClientSecret) =>
        identity
            .WithEnvironment("Tokens__Issuer", Issuer)
            .WithEnvironment("Tokens__SigningKey", signingKey)
            .WithEnvironment("Tokens__Clients__web__Secret", webClientSecret);

    // The service asks Identity for the public key, internally over HTTP. The issuer comes along in Identity's discovery
    // document, so ValidIssuer stays dotnet-user-jwts from appsettings.Development.json and dev tokens keep working.
    public static IResourceBuilder<ProjectResource> WithTokenValidation(
        this IResourceBuilder<ProjectResource> service, IResourceBuilder<ProjectResource> identity) =>
        service
            .WithEnvironment("Authentication__Schemes__Bearer__Authority", identity.GetEndpoint("http"))
            .WithEnvironment("Authentication__Schemes__Bearer__RequireHttpsMetadata", "false");
}

// A new P-256 key, PKCS#8 in Base64, for a parameter without a value yet.
internal sealed class SigningKeyDefault : ParameterDefault
{
    public override string GetDefaultValue()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }

    // The AppHost is never published as a manifest, the server gets its key from the environment.
    public override void WriteToManifest(ManifestPublishingContext context)
    {
    }
}