using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Edvaniq.Gateway.IntegrationTests;

// Stands in for a service behind the gateway. A real Kestrel on a free port, because YARP forwards over the network
// and not into a TestServer. Answers every request with what arrived, /slow only after 30 seconds.
public sealed class StubService : IAsyncDisposable
{
    private readonly WebApplication app;

    private StubService(WebApplication app)
    {
        this.app = app;
    }

    // Kestrel replaces port 0 with the port it got once it has started.
    public string Address => app.Urls.Single();

    public static async Task<StubService> StartAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.MapGet("/slow", async (CancellationToken cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Results.Ok();
        });
        app.Map("{**path}", (HttpRequest request) =>
            new Echo(name, request.Method, request.Path, request.Headers.Authorization.ToString()));

        await app.StartAsync(TestContext.Current.CancellationToken);
        return new StubService(app);
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}

public sealed record Echo(string Service, string Method, string Path, string Authorization);