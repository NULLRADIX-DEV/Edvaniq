using System.Globalization;
using System.Text.Json.Serialization;

using Edvaniq.Services.Identity.Application;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.Api.Tokens;

// Identity as the issuer of all tokens (docs/tokens.md), with the endpoints a standard client and the services expect:
// the token endpoint (OAuth 2.0 client credentials and password, RFC 6749), the discovery document a service reads from its
// Authority, and the key set the document points to. Only the token endpoint is routed through the gateway.
internal static class TokenEndpoints
{
    private const string DiscoveryPath = "/.well-known/openid-configuration";
    private const string KeySetPath = "/.well-known/jwks";
    private const string TokenPath = "/connect/token";
    private const string ClientCredentials = "client_credentials";
    private const string Password = "password";

    public static TBuilder AddTokenIssuer<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<TokenOptions>()
            .BindConfiguration(TokenOptions.Section)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TokenOptions>, TokenOptionsValidation>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SigningKey>();
        builder.Services.AddSingleton<TokenIssuer>();
        builder.Services.AddSingleton<LoginThrottle>();
        return builder;
    }

    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        // All three are open to anyone by design, without a token: they say so, so a token check added later (#244) keeps
        // them open, and the isolation check (#271) leaves them out while every new endpoint needs an entry there.
        // The addresses are built from the request, so a service gets back the address it reached Identity under.
        app.MapGet(DiscoveryPath, (HttpRequest request, IOptions<TokenOptions> options) =>
        {
            var baseAddress = $"{request.Scheme}://{request.Host}{request.PathBase}";
            return new Discovery(
                options.Value.Issuer,
                baseAddress + KeySetPath,
                baseAddress + TokenPath,
                [ClientCredentials, Password],
                ["client_secret_post"]);
        }).AllowAnonymous();

        app.MapGet(KeySetPath, (SigningKey key) => new KeySet([key.PublicKey])).AllowAnonymous();

        app.MapPost(TokenPath, IssueAsync).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> IssueAsync(
        HttpContext context, TokenIssuer issuer, LoginThrottle throttle, CancellationToken cancellationToken)
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

        var grantType = form["grant_type"].ToString();
        if (grantType is not (ClientCredentials or Password))
        {
            return Error(string.IsNullOrEmpty(grantType) ? "invalid_request" : "unsupported_grant_type");
        }

        var clientId = form["client_id"].ToString();
        var client = issuer.FindClient(clientId, form["client_secret"].ToString());
        if (client is null)
        {
            return Error("invalid_client");
        }

        if (grantType == ClientCredentials)
        {
            return TypedResults.Ok(new TokenResponse(issuer.Issue(clientId, client), "Bearer", (int)issuer.Lifetime.TotalSeconds));
        }

        // The login of a team account (RFC 6749, section 4.3), only for a client that may ask for it.
        if (!client.AllowPassword)
        {
            return Error("unauthorized_client");
        }

        var username = form["username"].ToString();
        var password = form["password"].ToString();
        if (username.Length == 0 || password.Length == 0)
        {
            return Error("invalid_request");
        }

        if (!throttle.TryAttempt(username, out var retryAfter))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return TypedResults.Json(new TokenError("too_many_attempts"), statusCode: StatusCodes.Status429TooManyRequests);
        }

        // Resolved only here: the database comes with it, and a client's own token must not depend on it.
        var accounts = context.RequestServices.GetRequiredService<AccountService>();
        var account = await accounts.AuthenticateAsync(username, password, cancellationToken);

        // A wrong name and a wrong password get the same answer.
        return account is not null
            ? TypedResults.Ok(new TokenResponse(
                issuer.IssueForAccount(account.Id, account.UsesTestDouble, clientId), "Bearer", (int)issuer.UserLifetime.TotalSeconds))
            : Error("invalid_grant");
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