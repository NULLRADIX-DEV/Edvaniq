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

    public async Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(UserPath, cancellationToken);
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

    public async Task<SignInOutcome> SignInAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                LoginPath, new Credentials(name, password), SessionJson.Default.Credentials, cancellationToken);
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => new SignInOutcome(SignInResult.SignedIn),
                HttpStatusCode.Unauthorized => new SignInOutcome(SignInResult.WrongCredentials),
                HttpStatusCode.TooManyRequests => new SignInOutcome(
                    SignInResult.TooManyAttempts, response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1)),
                _ => new SignInOutcome(SignInResult.Unavailable, TraceId: await ProblemTrace.ReadAsync(response, cancellationToken)),
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

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Credentials))]
internal sealed partial class SessionJson : JsonSerializerContext;