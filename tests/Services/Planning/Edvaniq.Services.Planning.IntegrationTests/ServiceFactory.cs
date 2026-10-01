using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// The test harness: the service as it runs, with a fresh MySQL database of its own that has all migrations applied,
// and trusting the tokens of TestTokens instead of those of the real issuer. One per test class (IClassFixture).
public class ServiceFactory(MySqlTestServer mysql) : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await mysql.CreateDatabaseAsync(InfrastructureExtensions.DatabaseName);

        // The same synchronous call the migrate step makes. Services starts the service with the new database.
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PlanningDbContext>().Database.Migrate();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .UseSetting($"ConnectionStrings:{InfrastructureExtensions.DatabaseName}", ConnectionString)
            .ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidIssuer = null;
                    options.TokenValidationParameters.ValidIssuers = [TestTokens.Issuer];
                    options.TokenValidationParameters.IssuerSigningKeys = [TestTokens.SigningKey];
                }));
}
