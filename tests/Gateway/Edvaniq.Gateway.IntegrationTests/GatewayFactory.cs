using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Edvaniq.Gateway.IntegrationTests;

// The gateway as it runs. Where the route planning leads is set only by configuration, the same keys the server
// would set: here to a stub instead of the service discovery name planning-api.
public sealed class GatewayFactory(string planningAddress, TimeSpan? planningTimeout = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReverseProxy:Clusters:planning:Destinations:planning-api:Address", planningAddress);

        if (planningTimeout is { } timeout)
        {
            builder.UseSetting("ReverseProxy:Clusters:planning:HttpRequest:ActivityTimeout", timeout.ToString("c"));
        }
    }
}