using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Gateway.IntegrationTests;

// The gateway as it runs. Where the routes lead is set only by configuration, the same keys the server would set: here
// to stubs instead of the service discovery names planning-api and identity-api.
public sealed class GatewayFactory(
    string planningAddress, TimeSpan? planningTimeout = null, string? identityAddress = null, Action<IWebHostBuilder>? configure = null)
    : WebApplicationFactory<Program>
{
    // What the gateway logs.
    public TestLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReverseProxy:Clusters:planning:Destinations:planning-api:Address", planningAddress);
        builder.UseSetting("ReverseProxy:Clusters:identity:Destinations:identity-api:Address", identityAddress ?? "http://127.0.0.1:9");
        builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(Logs));

        if (planningTimeout is { } timeout)
        {
            builder.UseSetting("ReverseProxy:Clusters:planning:HttpRequest:ActivityTimeout", timeout.ToString("c"));
        }

        configure?.Invoke(builder);
    }
}