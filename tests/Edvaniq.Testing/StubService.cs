using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using Xunit;

namespace Edvaniq.Testing;

// Stands in for a process behind a proxy, the gateway or a service. A real Kestrel on a free port, because YARP
// forwards over the network and not into a TestServer. Answers every request with what arrived, with SetsCookie also
// trying to set a cookie in the browser. /slow answers only after 30 seconds. POST /identity/connect/token answers like
// Identity behind the gateway, with the tokens stub-token-1, stub-token-2 and so on for a client and UserToken for the
// login of UserName with Password. Answer sets a body for a request that should not be echoed.
public sealed class StubService : IAsyncDisposable
{
    public const string TokenPath = "/identity/connect/token";
    public const string UserName = "ben";
    public const string Password = "richtig-langes-passwort";

    // Shaped and sized like a token of Identity for a login, so a test can look for it in cookie, page and log.
    public static readonly string UserToken = BuildUserToken();

    private WebApplication? app;
    private int requests;
    private int tokenRequests;
    private int loginRequests;
    private int tokenStatus = StatusCodes.Status200OK;
    private int loginStatus = StatusCodes.Status200OK;
    private readonly ConcurrentDictionary<(string Method, string Path), object> answers = new();
    private readonly ConcurrentDictionary<(string Method, string Path), Failure> failures = new();

    private StubService()
    {
    }

    // Kestrel replaces port 0 with the port it got once it has started.
    public string Address => app!.Urls.Single();

    // How many requests got this far, to prove that a proxy stopped one before it. Token requests count too.
    public int Requests => Volatile.Read(ref requests);

    public int TokenRequests => Volatile.Read(ref tokenRequests);

    // Logins of a team member (password grant). They do not count as token requests.
    public int LoginRequests => Volatile.Read(ref loginRequests);

    public IReadOnlyDictionary<string, string> LastLoginRequest { get; private set; } = new Dictionary<string, string>();

    // How logins are answered: 200 lets UserName with Password in and refuses everything else like Identity, 429 says
    // too many attempts with Retry-After 42, any other status comes without a body.
    public int LoginStatus
    {
        get => Volatile.Read(ref loginStatus);
        set => Volatile.Write(ref loginStatus, value);
    }

    // The form of the last token request.
    public IReadOnlyDictionary<string, string> LastTokenRequest { get; private set; } = new Dictionary<string, string>();

    // The traceparent header of the last request, empty without one. It shows whether the caller passed its trace on.
    public string LastTraceParent { get; private set; } = "";

    // Whether every echo also tries to set a cookie in the browser.
    public bool SetsCookie { get; set; }

    // How token requests are answered: 200 with a token, 400 like Identity for a wrong secret, or any other status
    // without a body, such as 503 while Identity is down.
    public int TokenStatus
    {
        get => Volatile.Read(ref tokenStatus);
        set => Volatile.Write(ref tokenStatus, value);
    }

    // Answers this request with the body as JSON instead of the echo, for a test that needs the service to answer like
    // the real one, such as Planning with a ping.
    public void Answer(string method, string path, object body) => answers[(method, path)] = body;

    // Answers this request with the status and body as they are, an empty body without a content type, and with headers
    // that tell how the service is built, like a service that fails on its own.
    public void Fail(string method, string path, int status, string? contentType = null, string body = "") =>
        failures[(method, path)] = new Failure(status, contentType, body);

    public static async Task<StubService> StartAsync(string name)
    {
        var stub = new StubService();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.Use((context, next) =>
        {
            Interlocked.Increment(ref stub.requests);
            stub.LastTraceParent = context.Request.Headers["traceparent"].ToString();
            return next(context);
        });
        app.MapGet("/slow", async (CancellationToken cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Results.Ok();
        });
        app.MapPost(TokenPath, async (HttpRequest request, HttpResponse response) =>
        {
            var form = await request.ReadFormAsync();
            if (form["grant_type"] == "password")
            {
                Interlocked.Increment(ref stub.loginRequests);
                stub.LastLoginRequest = form.ToDictionary(field => field.Key, field => field.Value.ToString());
                switch (stub.LoginStatus)
                {
                    case StatusCodes.Status200OK when form["username"] == UserName && form["password"] == Password:
                        return Results.Ok(new Dictionary<string, object>
                        {
                            ["access_token"] = UserToken,
                            ["token_type"] = "Bearer",
                            ["expires_in"] = 3600,
                        });
                    case StatusCodes.Status200OK:
                        return Results.BadRequest(new Dictionary<string, string> { ["error"] = "invalid_grant" });
                    case StatusCodes.Status429TooManyRequests:
                        response.Headers.RetryAfter = "42";
                        return Results.Json(new Dictionary<string, string> { ["error"] = "too_many_attempts" }, statusCode: 429);
                    case var status:
                        return Results.StatusCode(status);
                }
            }

            var number = Interlocked.Increment(ref stub.tokenRequests);
            stub.LastTokenRequest = form.ToDictionary(field => field.Key, field => field.Value.ToString());
            return stub.TokenStatus switch
            {
                StatusCodes.Status200OK => Results.Ok(new Dictionary<string, object>
                {
                    ["access_token"] = $"stub-token-{number}",
                    ["token_type"] = "Bearer",
                    ["expires_in"] = 300,
                }),
                StatusCodes.Status400BadRequest => Results.BadRequest(new Dictionary<string, string> { ["error"] = "invalid_client" }),
                var status => Results.StatusCode(status),
            };
        });
        app.Map("{**path}", (HttpRequest request, HttpResponse response) =>
        {
            if (stub.failures.TryGetValue((request.Method, request.Path.Value ?? ""), out var failure))
            {
                response.Headers["X-Powered-By"] = "stub-framework";
                return failure.ContentType is null
                    ? Results.StatusCode(failure.Status)
                    : Results.Text(failure.Body, failure.ContentType, statusCode: failure.Status);
            }

            if (stub.SetsCookie)
            {
                response.Cookies.Append("backend", "set-by-the-backend");
            }

            if (stub.answers.TryGetValue((request.Method, request.Path.Value ?? ""), out var answer))
            {
                return Results.Ok(answer);
            }

            return Results.Ok(new Echo(
                name,
                request.Method,
                request.Path,
                request.Headers.Authorization.ToString(),
                request.Headers.Cookie.ToString()));
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        stub.app = app;
        return stub;
    }

    public ValueTask DisposeAsync() => app?.DisposeAsync() ?? ValueTask.CompletedTask;

    private static string BuildUserToken()
    {
        static string Part(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Part("""{"alg":"ES256","kid":"6c2f3e0b9a1d4c7e8f5a2b3c4d5e6f70","typ":"at+jwt"}""");
        var payload = Part(
            $$"""{"iss":"https://edvaniq.example.org/identity","aud":"edvaniq-api","sub":"{{Guid.NewGuid()}}","client_id":"web","jti":"{{Guid.NewGuid():N}}","iat":{{now}},"nbf":{{now}},"exp":{{now + 3600}}}""");
        return $"{header}.{payload}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(64))}";
    }

    private sealed record Failure(int Status, string? ContentType, string Body);
}

public sealed record Echo(string Service, string Method, string Path, string Authorization, string Cookie);