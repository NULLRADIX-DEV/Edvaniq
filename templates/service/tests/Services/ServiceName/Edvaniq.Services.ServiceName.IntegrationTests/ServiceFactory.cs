using Edvaniq.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

// The service as it runs, except that it trusts the tokens of TestTokens instead of those of the real issuer.
public class ServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidIssuer = null;
                options.TokenValidationParameters.ValidIssuers = [TestTokens.Issuer];
                options.TokenValidationParameters.IssuerSigningKeys = [TestTokens.SigningKey];
            }));
}
