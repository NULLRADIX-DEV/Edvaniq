using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Edvaniq.Web;

// POST /session/login with name and password as JSON. The web host asks Identity for the user's token (password grant,
// docs/tokens.md) with its own client secret and keeps the token in the session cookie. The browser only learns how it
// went: 204 signed in, 401 name or password wrong (the same for both, nobody learns whether an account exists), 429 too
// many attempts with Retry-After, 502 when Identity gave no usable answer. Neither password nor token gets into a log.
internal static class WebLogin
{
    public const string RateLimitPolicy = "login";

    public static IServiceCollection AddWebLogin(this IServiceCollection services)
    {
        services.AddLoginLimit();

        // No retries: each one would count as another attempt against the account's limit at Identity. The call is
        // still marked as evaluation, a later version may rename it.
#pragma warning disable EXTEXP0001
        services.AddHttpClient(IdentityLogin.ClientName).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddSingleton<IdentityLogin>();
        return services;
    }

    public static async Task<IResult> LoginAsync(
        LoginRequest request, HttpContext context, IdentityLogin identity, TimeProvider time, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrEmpty(request.Password))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        // Taken before the request, so the time the request takes counts against the token.
        var requestedAt = time.GetUtcNow();
        var result = await identity.SignInAsync(request.Name, request.Password, cancellationToken);
        switch (result.Outcome)
        {
            case LoginOutcome.SignedIn:
                await WebSession.SignInUserAsync(context, result.Token!, requestedAt + result.Lifetime);
                return Results.NoContent();
            case LoginOutcome.WrongCredentials:
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            case LoginOutcome.TooManyAttempts:
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(result.Lifetime.TotalSeconds)).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            default:
                return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

internal sealed record LoginRequest(string? Name, string? Password);

internal enum LoginOutcome
{
    SignedIn,
    WrongCredentials,
    TooManyAttempts,
    Unavailable,
}

// Token and lifetime for a login, the wait for too many attempts.
internal sealed record LoginResult(LoginOutcome Outcome, string? Token = null, TimeSpan Lifetime = default);

// The password grant at Identity through the gateway, with the endpoint and client of the section BackendToken. A login
// gets 9 seconds, so the browser hears back before its own timeout.
internal sealed class IdentityLogin(
    IHttpClientFactory httpClientFactory, IOptions<BackendTokenOptions> options, ILogger<IdentityLogin> logger)
{
    public const string ClientName = "identity-login";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(9);

    public async Task<LoginResult> SignInAsync(string name, string password, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        var settings = options.Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = name,
            ["password"] = password,
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
        });

        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.PostAsync(settings.TokenEndpoint, form, timeout.Token);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var token = await response.Content.ReadFromJsonAsync<TokenAnswer>(timeout.Token);
                if (!string.IsNullOrEmpty(token?.AccessToken) && token.ExpiresIn > 0)
                {
                    return new LoginResult(LoginOutcome.SignedIn, token.AccessToken, TimeSpan.FromSeconds(token.ExpiresIn));
                }

                logger.LogWarning("Identity answered a login without a usable token.");
                return new LoginResult(LoginOutcome.Unavailable);
            }

            if (status == StatusCodes.Status429TooManyRequests)
            {
                return new LoginResult(LoginOutcome.TooManyAttempts, Lifetime: response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            }

            var error = status == StatusCodes.Status400BadRequest ? await ErrorAsync(response, timeout.Token) : null;
            if (error is "invalid_grant" or "invalid_request")
            {
                return new LoginResult(LoginOutcome.WrongCredentials);
            }

            logger.LogWarning("Identity refused a login with {Status} {Error}.", status, error);
            return new LoginResult(LoginOutcome.Unavailable);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or OperationCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "No answer from Identity for a login.");
            return new LoginResult(LoginOutcome.Unavailable);
        }
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<TokenError>(cancellationToken))?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record TokenAnswer(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record TokenError([property: JsonPropertyName("error")] string? Error);
}