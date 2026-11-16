using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Edvaniq.Web;

// POST /session/login with name and password as JSON, the name a team account's or the address of a user account (#251).
// The web host asks Identity for the user's token (password grant, docs/tokens.md) with its own client secret and keeps
// the token in the session cookie. The browser only learns how it went, as a problem with a code: 204 signed in, 401
// invalid_credentials (the same for a wrong login and a wrong password, nobody learns whether an account exists), 403
// unconfirmed or awaiting_approval (only after the right password), 429 too_many_attempts with Retry-After, 502 when
// Identity gave no usable answer. Neither password nor token gets into a log.
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
                return Refused(StatusCodes.Status401Unauthorized, "invalid_credentials", "Anmeldename oder Passwort stimmen nicht.");
            case LoginOutcome.Unconfirmed:
                return Refused(
                    StatusCodes.Status403Forbidden, "unconfirmed", "Bestätige zuerst deine E-Mail-Adresse mit dem Link aus unserer Mail.");
            case LoginOutcome.AwaitingApproval:
                return Refused(StatusCodes.Status403Forbidden, "awaiting_approval", "Dein Konto muss noch freigegeben werden.");
            case LoginOutcome.TooManyAttempts:
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(result.Lifetime.TotalSeconds)).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                return Refused(
                    StatusCodes.Status429TooManyRequests, "too_many_attempts", "Zu viele Versuche. Warte kurz und versuch es dann noch einmal.");
            default:
                return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static IResult Refused(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record LoginRequest(string? Name, string? Password);

internal enum LoginOutcome
{
    SignedIn,
    WrongCredentials,

    // The right password, but the address isn't confirmed yet (#250).
    Unconfirmed,

    // The right password, but a minor's account waits for the operator (#256).
    AwaitingApproval,

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

            var (error, reason) = status == StatusCodes.Status400BadRequest ? await ErrorAsync(response, timeout.Token) : (null, null);
            // Identity names a reason only after the right password (docs/tokens.md).
            if (error == "invalid_grant" && reason is "unconfirmed" or "awaiting_approval")
            {
                return new LoginResult(reason == "unconfirmed" ? LoginOutcome.Unconfirmed : LoginOutcome.AwaitingApproval);
            }

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

    private static async Task<(string? Error, string? Reason)> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await response.Content.ReadFromJsonAsync<TokenError>(cancellationToken);
            return (answer?.Error, answer?.Reason);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private sealed record TokenAnswer(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record TokenError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("reason")] string? Reason);
}