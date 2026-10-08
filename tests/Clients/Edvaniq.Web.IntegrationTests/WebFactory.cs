using Edvaniq.Web.Components;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Edvaniq.Web.IntegrationTests;

// The web host as it runs, with the gateway replaced by a stub. App stands for the web host's assembly: Program would
// be ambiguous, the WebAssembly client has one of its own.
public sealed class WebFactory(string gatewayAddress) : WebApplicationFactory<App>
{
    // The session cookie is Secure, so a client only sends it back over HTTPS.
    public HttpClient CreateBrowser(bool handleCookies = true) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
            AllowAutoRedirect = false,
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ReverseProxy:Clusters:gateway:Destinations:gateway:Address", gatewayAddress);
}