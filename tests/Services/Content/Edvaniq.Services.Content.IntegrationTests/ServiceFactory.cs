using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.IntegrationTests;

// The test harness: the service as it runs, with a fresh MySQL database of its own that has all migrations applied,
// trusting the tokens of TestTokens instead of those of the real issuer, and sending its events through a broker inside
// the process instead of NATS. One per test class (IClassFixture).
public class ServiceFactory(MySqlTestServer mysql) : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";

    // What the service logs, for the tests that look for a log line.
    public TestLogs Logs { get; } = new();

    // Answers the worker's question pools in place of Knowledge. The Api itself never asks Knowledge.
    public KnowledgeStub Knowledge { get; } = new();

    // Answers the Api's question for the default goal in place of Planning, behind the real client.
    public PlanningStub Planning { get; } = new();

    // The file store of this factory, empty at the start and gone at the end.
    public string FilesDirectory { get; } = Path.Combine(Path.GetTempPath(), "edvaniq-content-files", Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await mysql.CreateDatabaseAsync(InfrastructureExtensions.DatabaseName);

        // The same synchronous call the migrate step makes. Services starts the service with the new database.
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ContentDbContext>().Database.Migrate();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .UseSetting($"ConnectionStrings:{InfrastructureExtensions.DatabaseName}", ConnectionString)
            .UseSetting("services:planning-api:http:0", "http://planning-api")
            .UseSetting("FileStorage:Directory", FilesDirectory)
            .ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.TokenValidationParameters.ValidateIssuer = true;
                        options.TokenValidationParameters.ValidIssuer = null;
                        options.TokenValidationParameters.ValidIssuers = [TestTokens.Issuer];
                        options.TokenValidationParameters.IssuerSigningKeys = [TestTokens.SigningKey];
                    });
                services.AddInMemoryTransport();
                services.AddContentTestDouble();
                services.AddSingleton<ILoggerProvider>(Logs);
                services.AddSingleton<IKnowledgeTopics>(Knowledge);
                services.AddHttpClient<ILearningGoals, PlanningLearningGoals>().ConfigurePrimaryHttpMessageHandler(() => Planning);
            });

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(FilesDirectory))
        {
            Directory.Delete(FilesDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}