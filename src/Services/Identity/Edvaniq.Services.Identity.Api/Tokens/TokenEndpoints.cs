using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.Api.Tokens;

// Identity as the issuer of the technical token, with the endpoints a standard client and the services expect:
// the token endpoint (OAuth 2.0 client credentials, RFC 6749), the discovery document a service reads from its
// Authority, and the key set the document points to. Only the token endpoint is routed through the gateway.
internal static class TokenEndpoints
{
    private const string DiscoveryPath = "/.well-known/openid-configuration";
    private const string KeySetPath = "/.well-known/jwks";
    private const string TokenPath = "/connect/token";
    private const string ClientCredentials = "client_credentials";

    public static TBuilder AddTokenIssuer<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<TokenOptions>()
            .BindConfiguration(TokenOptions.Section)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TokenOptions>, TokenOptionsValidation>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SigningKey>();
        builder.Services.AddSingleton<TokenIssuer>();
        return builder;
    }

    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        // The addresses are built from the request, so a service gets back the address it reached Identity under.
        app.MapGet(DiscoveryPath, (HttpRequest request, IOptions<TokenOptions> options) =>
        {
            var baseAddress = $"{request.Scheme}://{request.Host}{request.PathBase}";
            return new Discovery(
                options.Value.Issuer,
                baseAddress + KeySetPath,
                baseAddress + TokenPath,
                [ClientCredentials],
                ["client_secret_post"]);
        });

        app.MapGet(KeySetPath, (SigningKey key) => new KeySet([key.PublicKey]));

        app.MapPost(TokenPath, IssueAsync);

        return app;
    }

    private static async Task<IResult> IssueAsync(HttpContext context, TokenIssuer issuer, CancellationToken cancellationToken)
    {
        // No answer of the token endpoint may end up in a cache (RFC 6749, section 5.1).
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";

        if (!context.Request.HasFormContentType)
        {
            return Error("invalid_request");
        }

        // A form beyond the limits of ASP.NET Core or a broken multipart body would otherwise end as a 500 with a stack
        // trace in the log, as often as anyone sends one.
        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
        {
            return Error("invalid_request");
        }

        if (form["grant_type"] != ClientCredentials)
        {
            return Error(string.IsNullOrEmpty(form["grant_type"]) ? "invalid_request" : "unsupported_grant_type");
        }

        var clientId = form["client_id"].ToString();
        if (!issuer.IsClient(clientId, form["client_secret"].ToString()))
        {
            return Error("invalid_client");
        }

        return TypedResults.Ok(new TokenResponse(issuer.Issue(clientId), "Bearer", (int)issuer.Lifetime.TotalSeconds));
    }

    private static IResult Error(string error) => TypedResults.BadRequest(new TokenError(error));
}

internal sealed record Discovery(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("grant_types_supported")] IReadOnlyList<string> GrantTypesSupported,
    [property: JsonPropertyName("token_endpoint_auth_methods_supported")] IReadOnlyList<string> TokenEndpointAuthMethodsSupported);

internal sealed record KeySet([property: JsonPropertyName("keys")] IReadOnlyList<PublicKey> Keys);

internal sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

internal sealed record TokenError([property: JsonPropertyName("error")] string Error);