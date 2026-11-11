using Edvaniq.Testing;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.EventFlowTests;

// One service as it runs, with the settings given (its database and the broker), trusting the tokens of TestTokens.
// Services adds what a test hands the service in place of another one.
public sealed class ServiceHost<TEntryPoint>(IReadOnlyDictionary<string, string> settings, Action<IServiceCollection>? services = null)
    : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(collection =>
        {
            // Every scheme, the internal one too.
            collection.PostConfigureAll<JwtBearerOptions>(
                options =>
                {
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidIssuer = null;
                    options.TokenValidationParameters.ValidIssuers = [TestTokens.Issuer];
                    options.TokenValidationParameters.IssuerSigningKeys = [TestTokens.SigningKey];
                });
            services?.Invoke(collection);
        });
    }
}