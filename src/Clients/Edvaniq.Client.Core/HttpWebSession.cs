using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to /session of the web host. The HttpClient carries the base address of the host and has to keep the session
// cookie, as the browser does, and share it with the ping. Its timeout is longer than the host's own wait for Identity.
public sealed class HttpWebSession(HttpClient http) : IWebSession
{
    public const string Path = "session";
    public const string UserPath = "session/user";
    public const string LoginPath = "session/login";

    // A host that says someone is signed in without naming the account still counts, with an empty key.
    public async Task<string?> AccountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(UserPath, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            try
            {
                return (await response.Content.ReadFromJsonAsync(SessionJson.Default.SessionUser, cancellationToken))?.Account ?? "";
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return "";
            }
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<SignInOutcome> SignInAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                LoginPath, new Credentials(name, password), SessionJson.Default.Credentials, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new SignInOutcome(SignInResult.SignedIn);
            }

            // 401 and 429 say enough by their status: too_many_attempts and locked (#252) only differ in the wait.
            var refusal = await LoginRefusal.ReadAsync(response, cancellationToken);
            return (response.StatusCode, refusal.Code) switch
            {
                (HttpStatusCode.Unauthorized, _) => new SignInOutcome(SignInResult.WrongCredentials),
                (HttpStatusCode.TooManyRequests, _) => new SignInOutcome(
                    SignInResult.TooManyAttempts, response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1)),
                (HttpStatusCode.Forbidden, "unconfirmed") => new SignInOutcome(
                    SignInResult.Unconfirmed, Title: ProblemTitle.Check(refusal.Title)),
                (HttpStatusCode.Forbidden, "awaiting_approval") => new SignInOutcome(
                    SignInResult.AwaitingApproval, Title: ProblemTitle.Check(refusal.Title)),
                _ => new SignInOutcome(SignInResult.Unavailable, TraceId: ProblemTrace.Check(refusal.TraceId)),
            };
        }
        catch (HttpRequestException)
        {
            return new SignInOutcome(SignInResult.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SignInOutcome(SignInResult.Unavailable);
        }
    }

    public async Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.DeleteAsync(Path, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

internal sealed record Credentials(string Name, string Password);

internal sealed record SessionUser(string? Account);

// Why the host refused a login. Anything but a readable problem counts as none.
internal sealed record LoginRefusal(string? Code = null, string? Title = null, string? TraceId = null)
{
    public static async Task<LoginRefusal> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return new LoginRefusal();
        }

        try
        {
            return await response.Content.ReadFromJsonAsync(SessionJson.Default.LoginRefusal, cancellationToken) ?? new LoginRefusal();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return new LoginRefusal();
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Credentials))]
[JsonSerializable(typeof(SessionUser))]
[JsonSerializable(typeof(LoginRefusal))]
internal sealed partial class SessionJson : JsonSerializerContext;