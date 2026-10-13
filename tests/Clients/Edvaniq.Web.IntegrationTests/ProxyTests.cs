using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Edvaniq.Web.IntegrationTests;

// On the server the host's nginx ends HTTPS and reaches the web host through the gateway of the app's network. Only
// from that network the web host takes scheme and client address from X-Forwarded-Proto and X-Forwarded-For, nobody
// else can pretend to be the proxy.
public sealed class ProxyTests
{
    private const string AppNetwork = "10.1.2.0/24";

    [Fact]
    public async Task FromTheAppsNetwork_SchemeAndClientComeFromTheProxy()
    {
        await using var web = Web();

        var context = await SendAsync(web, from: "10.1.2.1");

        Assert.Equal("https", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task FromAnywhereElse_TheHeadersAreIgnored()
    {
        await using var web = Web();

        var context = await SendAsync(web, from: "10.1.3.1");

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("10.1.3.1"), context.Connection.RemoteIpAddress);
    }

    // Started locally or in the AppHost there is no proxy and no network to trust.
    [Fact]
    public async Task WithoutANetwork_TheHeadersAreIgnored()
    {
        await using var web = new WebFactory("http://127.0.0.1:9");

        var context = await SendAsync(web, from: "10.1.2.1");

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal(IPAddress.Parse("10.1.2.1"), context.Connection.RemoteIpAddress);
    }

    private static WebFactory Web() => new("http://127.0.0.1:9", configure: builder =>
        builder.UseSetting("ForwardedHeaders:KnownNetworks:0", AppNetwork));

    // The request as nginx sends it: plain HTTP, the browser's address and scheme in the headers.
    private static Task<HttpContext> SendAsync(WebFactory web, string from) =>
        web.Server.SendAsync(context =>
        {
            context.Request.Method = "GET";
            context.Request.Scheme = "http";
            context.Request.Path = "/session";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Connection.RemoteIpAddress = IPAddress.Parse(from);
        }, TestContext.Current.CancellationToken);
}