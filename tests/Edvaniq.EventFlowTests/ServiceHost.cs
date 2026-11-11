using Edvaniq.Testing;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.EventFlowTests;

// One service as it runs, with the settings given (its database and the broker), trusting the tokens of TestTokens.
public sealed class ServiceHost<TEntryPoint>(IReadOnlyDictionary<string, string> settings) : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidIssuer = null;
                    options.TokenValidationParameters.ValidIssuers = [TestTokens.Issuer];
                    options.TokenValidationParameters.IssuerSigningKeys = [TestTokens.SigningKey];
                }));
    }
}