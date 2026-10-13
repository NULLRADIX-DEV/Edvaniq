using System.Security.Cryptography;

using Edvaniq.Testing;
using Edvaniq.Web.Components;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Web.IntegrationTests;

// The web host as it runs, with the gateway replaced by a stub. App stands for the web host's assembly: Program would
// be ambiguous, the WebAssembly client has one of its own. The client secret exists only for this test run.
public sealed class WebFactory(string gatewayAddress, TimeProvider? time = null, bool withClientSecret = true)
    : WebApplicationFactory<App>
{
    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    // The session cookie is Secure, so a client only sends it back over HTTPS.
    public HttpClient CreateBrowser(bool handleCookies = true) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
            AllowAutoRedirect = false,
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReverseProxy:Clusters:gateway:Destinations:gateway:Address", gatewayAddress);
        builder.UseSetting("BackendToken:TokenEndpoint", gatewayAddress + StubService.TokenPath);
        builder.UseSetting("BackendToken:ClientSecret", withClientSecret ? ClientSecret : "");

        if (time is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(time));
        }
    }
}