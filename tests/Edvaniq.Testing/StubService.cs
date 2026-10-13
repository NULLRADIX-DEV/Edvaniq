using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using Xunit;

namespace Edvaniq.Testing;

// Stands in for a process behind a proxy, the gateway or a service. A real Kestrel on a free port, because YARP
// forwards over the network and not into a TestServer. Answers every request with what arrived, with SetsCookie also
// trying to set a cookie in the browser. /slow answers only after 30 seconds. POST /identity/connect/token answers like
// Identity behind the gateway, with the tokens stub-token-1, stub-token-2 and so on.
public sealed class StubService : IAsyncDisposable
{
    public const string TokenPath = "/identity/connect/token";

    private WebApplication? app;
    private int requests;
    private int tokenRequests;
    private int tokenStatus = StatusCodes.Status200OK;

    private StubService()
    {
    }

    // Kestrel replaces port 0 with the port it got once it has started.
    public string Address => app!.Urls.Single();

    // How many requests got this far, to prove that a proxy stopped one before it. Token requests count too.
    public int Requests => Volatile.Read(ref requests);

    public int TokenRequests => Volatile.Read(ref tokenRequests);

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
        app.MapPost(TokenPath, async (HttpRequest request) =>
        {
            var number = Interlocked.Increment(ref stub.tokenRequests);
            var form = await request.ReadFormAsync();
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
            if (stub.SetsCookie)
            {
                response.Cookies.Append("backend", "set-by-the-backend");
            }

            return new Echo(
                name,
                request.Method,
                request.Path,
                request.Headers.Authorization.ToString(),
                request.Headers.Cookie.ToString());
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        stub.app = app;
        return stub;
    }

    public ValueTask DisposeAsync() => app?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public sealed record Echo(string Service, string Method, string Path, string Authorization, string Cookie);