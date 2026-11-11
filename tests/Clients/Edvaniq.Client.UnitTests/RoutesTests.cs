using Bunit;
using Bunit.TestDoubles;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// A page with [Authorize] wants a login. In the client the router leads to the login and back, the public pages stay
// open to everyone.
public sealed class RoutesTests : BunitContext
{
    private readonly BunitAuthorizationContext authorization;

    public RoutesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IBackendStatus>(new FakeBackendStatus(true));
        Services.AddSingleton<IPing>(new FakePing(new PingOutcome(new PingResult(42, DateTimeOffset.UtcNow))));
        Services.AddSingleton<IWebSession>(new FakeWebSession());
        authorization = AddAuthorization();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    [Fact]
    public void ProtectedPage_WithoutLogin_LeadsToTheLogin_AndBack()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/systemstatus");

        Render<Routes>();

        Assert.Equal("http://localhost/login?returnUrl=%2Fsystemstatus", navigation.Uri);
    }

    [Fact]
    public void ProtectedPage_AfterLogin_IsThere()
    {
        authorization.SetAuthorized("signed-in");
        Services.GetRequiredService<NavigationManager>().NavigateTo("/systemstatus");

        var routes = Render<Routes>();

        Assert.Equal("Systemstatus", routes.Find("h1").TextContent);
    }

    [Theory]
    [InlineData("/", "Start")]
    [InlineData("/login", "Anmelden")]
    [InlineData("/datenschutz", "Datenschutzhinweis")]
    [InlineData("/open-source", "Open-Source-Hinweise")]
    [InlineData("/bausteine", "Bausteine")]
    public void PublicPages_NeedNoLogin(string path, string heading)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(path);

        var routes = Render<Routes>();

        Assert.Equal(heading, routes.Find("h1").TextContent);
        Assert.Equal("http://localhost" + path, navigation.Uri);
    }

    [Fact]
    public void WhileTheLoginIsChecked_ThePageSaysItIsLoading()
    {
        authorization.SetAuthorizing();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/systemstatus");

        var routes = Render<Routes>();

        Assert.Contains(routes.FindAll("main [role=status]"), status => status.TextContent.Trim() == "Wird geladen …");
    }
}

// The client asks the web host once and again after every login and logout.
public sealed class SessionAuthenticationStateTests
{
    [Fact]
    public async Task SignedIn_AsTheHostSays_AndRefreshedOnRequest()
    {
        var session = new FakeWebSession();
        var state = new SessionAuthenticationState(session);
        var changes = 0;
        state.AuthenticationStateChanged += _ => changes++;

        var before = await state.GetAuthenticationStateAsync();
        session.SignedIn = true;
        var cached = await state.GetAuthenticationStateAsync();
        await state.Refresh();
        var after = await state.GetAuthenticationStateAsync();

        Assert.False(before.User.Identity?.IsAuthenticated);
        Assert.False(cached.User.Identity?.IsAuthenticated);
        Assert.True(after.User.Identity?.IsAuthenticated);
        Assert.Equal(1, changes);
    }
}