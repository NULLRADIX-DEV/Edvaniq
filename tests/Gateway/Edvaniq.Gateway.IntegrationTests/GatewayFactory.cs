using Edvaniq.Testing;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Gateway.IntegrationTests;

// The gateway as it runs. Where the routes lead is set only by configuration, the same keys the server would set: here
// to stubs instead of the service discovery names like planning-api. services maps further clusters to stubs. The
// gateway accepts tokens from TestTokens, unless testKeys is false and the test sets up the keys itself.
public sealed class GatewayFactory(
    string planningAddress,
    TimeSpan? planningTimeout = null,
    string? identityAddress = null,
    Action<IWebHostBuilder>? configure = null,
    IReadOnlyDictionary<string, string>? services = null,
    bool testKeys = true)
    : WebApplicationFactory<Program>
{
    // What the gateway logs.
    public TestLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReverseProxy:Clusters:planning:Destinations:planning-api:Address", planningAddress);
        builder.UseSetting("ReverseProxy:Clusters:identity:Destinations:identity-api:Address", identityAddress ?? "http://127.0.0.1:9");
        foreach (var (cluster, address) in services ?? new Dictionary<string, string>())
        {
            builder.UseSetting($"ReverseProxy:Clusters:{cluster}:Destinations:{cluster}-api:Address", address);
        }

        builder.ConfigureTestServices(testServices =>
        {
            testServices.AddSingleton<ILoggerProvider>(Logs);
            if (testKeys)
            {
                testServices.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = null;
                    options.TokenValidationParameters.ValidIssuers = [TestTokens.Issuer];
                    options.TokenValidationParameters.IssuerSigningKeys = [TestTokens.SigningKey];
                });
            }
        });

        if (planningTimeout is { } timeout)
        {
            builder.UseSetting("ReverseProxy:Clusters:planning:HttpRequest:ActivityTimeout", timeout.ToString("c"));
        }

        configure?.Invoke(builder);
    }
}