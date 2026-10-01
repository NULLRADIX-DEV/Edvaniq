using System.Net;
using System.Net.Sockets;
using Edvaniq.Services.ServiceName.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

// The service starts and tells alive (/alive) apart from ready (/health), which also needs its own database.
public sealed class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Nothing listens on port 1, so the connection is refused at once.
    private const string UnreachableDatabase = "Server=127.0.0.1;Port=1;Database=servicenamedb;User ID=test";

    [Fact]
    public async Task Alive_ReportsHealthy()
    {
        using var client = factory.CreateClient();

        await AssertHealthAsync(client, "/alive", HttpStatusCode.OK, "Healthy");
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

        await using var service = WithDatabase($"Server=127.0.0.1;Port={port};Database=servicenamedb;User ID=test");
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
}
