using System.Net;
using System.Net.Http.Headers;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Web;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// The tokens from Identity pass the token check every service from the template runs, unchanged: AddTokenValidation
// with nothing but the contract from Authentication:Schemes:Bearer, as the server sets it. The service fetches the
// key from Identity itself, over its Authority.
public sealed class ServiceAcceptanceTests
{
    [Fact]
    public async Task Service_AcceptsTheToken_WithTheClientAsUser()
    {
        await using var identity = new IdentityFactory();
        await using var service = await StartServiceAsync(identity);

        using var response = await GetMeAsync(service, await identity.GetTokenAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IdentityFactory.ClientId, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    // The service also takes the issuer from Identity's discovery document. So it accepts Identity's tokens even if
    // its own ValidIssuer names someone else, as locally with dotnet-user-jwts from appsettings.Development.json.
    [Fact]
    public async Task Service_TakesTheIssuerFromIdentity()
    {
        await using var identity = new IdentityFactory();
        await using var service = await StartServiceAsync(identity, validIssuer: "dotnet-user-jwts");

        using var response = await GetMeAsync(service, await identity.GetTokenAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Service_RejectsARequestWithoutToken()
    {
        await using var identity = new IdentityFactory();
        await using var service = await StartServiceAsync(identity);

        using var response = await GetMeAsync(service, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Issued an hour ago, so expired even with the 5 minutes .NET tolerates for clocks that run apart.
    [Fact]
    public async Task Service_RejectsAnExpiredToken()
    {
        var anHourAgo = new TestTime();
        anHourAgo.Advance(TimeSpan.FromHours(-1));
        await using var identity = new IdentityFactory(time: anHourAgo);
        await using var service = await StartServiceAsync(identity);

        using var response = await GetMeAsync(service, await identity.GetTokenAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Same issuer and client, but a key the service does not get from its Identity.
    [Fact]
    public async Task Service_RejectsATokenSignedWithAnotherKey()
    {
        await using var identity = new IdentityFactory();
        await using var impostor = new IdentityFactory();
        await using var service = await StartServiceAsync(identity);

        using var response = await GetMeAsync(service, await impostor.GetTokenAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A service with the token check from BuildingBlocks.Web and GET /me like Planning. Its key requests go to
    // Identity's test server instead of over the network.
    private static async Task<WebApplication> StartServiceAsync(
        IdentityFactory identity, string validIssuer = IdentityFactory.Issuer)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.WebHost.UseSetting("Authentication:Schemes:Bearer:Authority", "http://localhost");
        builder.WebHost.UseSetting("Authentication:Schemes:Bearer:RequireHttpsMetadata", "false");
        builder.WebHost.UseSetting("Authentication:Schemes:Bearer:ValidIssuer", validIssuer);
        builder.WebHost.UseSetting("Authentication:Schemes:Bearer:ValidAudiences:0", "edvaniq-api");
        builder.AddTokenValidation();
        // Configure, not PostConfigure: the bearer's own post-configure step builds the key client from this handler.
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.BackchannelHttpHandler = identity.Server.CreateHandler());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (ICurrentUser user) => user.Id);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static Task<HttpResponseMessage> GetMeAsync(WebApplication service, string? token)
    {
        var client = service.GetTestClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client.GetAsync("/me", TestContext.Current.CancellationToken);
    }
}