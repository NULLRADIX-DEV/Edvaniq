using System.Net;
using System.Net.Sockets;
using Edvaniq.Services.Planning.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Planning.IntegrationTests;

// The service starts and tells alive (/alive) apart from ready (/health), which also needs its own database.
public sealed class HealthTests(WebApplicationFactory<Program> factory, ServiceFactory service)
    : IClassFixture<WebApplicationFactory<Program>>, IClassFixture<ServiceFactory>
{
    // Nothing listens on port 1, so the connection is refused at once.
    private const string UnreachableDatabase = "Server=127.0.0.1;Port=1;Database=planningdb;User ID=test";

    [Fact]
    public async Task Alive_ReportsHealthy()
    {
        using var client = factory.CreateClient();

        await AssertHealthAsync(client, "/alive", HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task Health_WithDatabase_ReportsHealthy()
    {
        using var client = service.CreateClient();

        await AssertHealthAsync(client, "/health", HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task Health_WithUnreachableDatabase_ReportsUnhealthyButAlive()
    {
        await using var service = WithDatabase(UnreachableDatabase);
        using var client = service.CreateClient();

        await AssertHealthAsync(client, "/health", HttpStatusCode.ServiceUnavailable, "Unhealthy");
        await AssertHealthAsync(client, "/alive", HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public async Task Health_WithSilentDatabase_ReportsUnhealthyInTime()
    {
        // Accepts connections but never answers, like a frozen database container.
        using var silentDatabase = new TcpListener(IPAddress.Loopback, 0);
        silentDatabase.Start();
        var port = ((IPEndPoint)silentDatabase.LocalEndpoint).Port;

        await using var service = WithDatabase($"Server=127.0.0.1;Port={port};Database=planningdb;User ID=test");
        using var client = service.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        await AssertHealthAsync(client, "/health", HttpStatusCode.ServiceUnavailable, "Unhealthy");
    }

    [Fact]
    public async Task Health_WithoutConnectionString_ReportsUnhealthy()
    {
        await using var service = WithDatabase("");
        using var client = service.CreateClient();

        await AssertHealthAsync(client, "/health", HttpStatusCode.ServiceUnavailable, "Unhealthy");
    }

    [Fact]
    public async Task Health_OutsideDevelopment_AnswersWithoutToken()
    {
        // Outside Development health is middleware on the internal port, which the token check must not cover.
        await using var production = service.WithWebHostBuilder(builder => builder
            .UseEnvironment(Environments.Production)
            .ConfigureTestServices(services => services.AddSingleton<IStartupFilter, InternalPortFilter>()));
        using var client = production.CreateClient();

        await AssertHealthAsync(client, "/alive", HttpStatusCode.OK, "Healthy");
        await AssertHealthAsync(client, "/health", HttpStatusCode.OK, "Healthy");
    }

    [Fact]
    public void Start_WithoutIssuer_Fails()
    {
        // Outside Development the issuer comes only from the environment, here there is none.
        using var service = factory.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));

        var exception = Assert.ThrowsAny<OptionsValidationException>(() => service.CreateClient());
        Assert.Contains("ValidIssuer", exception.Message);
    }

    private WebApplicationFactory<Program> WithDatabase(string connectionString) =>
        factory.WithWebHostBuilder(builder =>
            builder.UseSetting($"ConnectionStrings:{InfrastructureExtensions.DatabaseName}", connectionString));

    private static async Task AssertHealthAsync(HttpClient client, string path, HttpStatusCode status, string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // The test server has no ports, so every request pretends to come in on the internal health port.
    private sealed class InternalPortFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.LocalPort = 8081;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
