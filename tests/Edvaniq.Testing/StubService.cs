using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using Xunit;

namespace Edvaniq.Testing;

// Stands in for a process behind a proxy, the gateway or a service. A real Kestrel on a free port, because YARP
// forwards over the network and not into a TestServer. Answers every request with what arrived. /slow answers only
// after 30 seconds, /set-cookie tries to set a cookie in the browser.
public sealed class StubService : IAsyncDisposable
{
    private WebApplication? app;
    private int requests;

    private StubService()
    {
    }

    // Kestrel replaces port 0 with the port it got once it has started.
    public string Address => app!.Urls.Single();

    // How many requests got this far, to prove that a proxy stopped one before it.
    public int Requests => Volatile.Read(ref requests);

    public static async Task<StubService> StartAsync(string name)
    {
        var stub = new StubService();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.Use((context, next) =>
        {
            Interlocked.Increment(ref stub.requests);
            return next(context);
        });
        app.MapGet("/slow", async (CancellationToken cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Results.Ok();
        });
        app.MapGet("/set-cookie", (HttpResponse response) =>
        {
            response.Cookies.Append("backend", "set-by-the-backend");
            return Results.NoContent();
        });
        app.Map("{**path}", (HttpRequest request) => new Echo(
            name,
            request.Method,
            request.Path,
            request.Headers.Authorization.ToString(),
            request.Headers.Cookie.ToString()));

        await app.StartAsync(TestContext.Current.CancellationToken);
        stub.app = app;
        return stub;
    }

    public ValueTask DisposeAsync() => app?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public sealed record Echo(string Service, string Method, string Path, string Authorization, string Cookie);