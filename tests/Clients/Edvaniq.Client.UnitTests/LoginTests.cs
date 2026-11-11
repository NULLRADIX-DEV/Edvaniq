using Bunit;
using Bunit.TestDoubles;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// The login page: name and password, a hint in plain German when it does not work, and back to where the user came from
// when it does.
public sealed class LoginTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private FakeWebSession session = new();

    public LoginTests()
    {
        Services.AddSingleton<IWebSession>(_ => session);
        AddAuthorization();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    [Fact]
    public void Fields_HaveLabels_AndTellTheBrowserWhatTheyAre()
    {
        var form = RenderForm(new FakeWebSession());

        Assert.Equal("Name", form.Find("label[for=login-name]").TextContent);
        Assert.Equal("Passwort", form.Find("label[for=login-password]").TextContent);
        Assert.Equal("username", form.Find("#login-name").GetAttribute("autocomplete"));
        Assert.Equal("password", form.Find("#login-password").GetAttribute("type"));
        Assert.Equal("current-password", form.Find("#login-password").GetAttribute("autocomplete"));
        Assert.Empty(form.Find("[role=alert]").TextContent.Trim());
    }

    [Fact]
    public void Success_LeadsBack_ToWhereTheUserCameFrom()
    {
        var session = new FakeWebSession();
        var form = RenderForm(session, "/systemstatus");

        SignIn(form, " ben ", "richtig-langes-passwort");

        Assert.Equal(("ben", "richtig-langes-passwort"), Assert.Single(session.Logins));
        Assert.Equal("http://localhost/systemstatus", Services.GetRequiredService<NavigationManager>().Uri);
    }

    // The way back stays on this site, whatever the link to the login said.
    [Theory]
    [InlineData(null)]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    public void Success_WithAForeignWayBack_LeadsToTheStartPage(string? returnUrl)
    {
        var form = RenderForm(new FakeWebSession(), returnUrl);

        SignIn(form);

        Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    // One hint for a wrong name and a wrong password: the page cannot tell them apart, and nobody learns whether an
    // account exists. The password goes, the name stays for the next try.
    [Fact]
    public void WrongCredentials_ShowAHint_AndClearThePassword()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.WrongCredentials)));

        SignIn(form);

        Assert.Equal(
            "Name oder Passwort stimmen nicht. Prüf beides und versuch es noch einmal.",
            form.Find("[role=alert] p").TextContent);
        Assert.Equal("ben", form.Find("#login-name").GetAttribute("value"));
        Assert.Equal("", form.Find("#login-password").GetAttribute("value"));
        Assert.Equal("http://localhost/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Theory]
    [InlineData(42, "Zu viele Versuche. Warte 42 Sekunden und versuch es dann noch einmal.")]
    [InlineData(60, "Zu viele Versuche. Warte eine Minute und versuch es dann noch einmal.")]
    [InlineData(61, "Zu viele Versuche. Warte 2 Minuten und versuch es dann noch einmal.")]
    public void TooManyAttempts_SayHowLongToWait(int seconds, string hint)
    {
        var form = RenderForm(new FakeWebSession(
            new SignInOutcome(SignInResult.TooManyAttempts, TimeSpan.FromSeconds(seconds))));

        SignIn(form);

        Assert.Equal(hint, form.Find("[role=alert] p").TextContent);
    }

    [Fact]
    public void Unavailable_SaysSo_AndNamesTheTrace()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unavailable, TraceId: TraceId)));

        SignIn(form);

        Assert.Contains("Die Anmeldung ist gerade nicht möglich.", form.Find("[role=alert]").TextContent);
        Assert.Equal(TraceId, form.Find("[role=alert] .request-id code").TextContent);
    }

    [Theory]
    [InlineData("", "richtig-langes-passwort")]
    [InlineData("  ", "richtig-langes-passwort")]
    [InlineData("ben", "")]
    public void MissingField_SendsNothing(string name, string password)
    {
        var session = new FakeWebSession();
        var form = RenderForm(session);

        SignIn(form, name, password);

        Assert.Empty(session.Logins);
        Assert.Equal("Gib deinen Namen und dein Passwort ein.", form.Find("[role=alert] p").TextContent);
    }

    // A second press while a login runs sends nothing, the button says what is going on.
    [Fact]
    public void SecondSubmit_WhileALoginRuns_SendsNothing()
    {
        var session = new FakeWebSession { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var form = RenderForm(session);

        SignIn(form);
        form.Find("form").Submit();

        Assert.Single(session.Logins);
        Assert.Equal("Anmeldung läuft …", form.Find("button").TextContent);
        session.Gate.SetResult();
        form.WaitForAssertion(() => Assert.EndsWith("/", Services.GetRequiredService<NavigationManager>().Uri));
    }

    // While prerendering there is no IWebSession to send a login with.
    [Fact]
    public void WhilePrerendering_ThePageSaysItIsLoading()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var page = Render<Login>();

        Assert.Equal("Anmelden", page.Find("h1").TextContent);
        Assert.Equal("Wird geladen …", page.Find("[role=status] .loading").TextContent);
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void ThePage_TakesTheWayBackFromTheAddress()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?returnUrl=%2Fsystemstatus");
        var page = Render<Login>();

        SignIn(page);

        Assert.Equal("http://localhost/systemstatus", Services.GetRequiredService<NavigationManager>().Uri);
    }

    private IRenderedComponent<LoginForm> RenderForm(FakeWebSession fake, string? returnUrl = null)
    {
        session = fake;
        return Render<LoginForm>(parameters => parameters.Add(form => form.ReturnTo, returnUrl));
    }

    private static void SignIn(IRenderedComponent<IComponent> form, string name = "ben", string password = "richtig-langes-passwort")
    {
        form.Find("#login-name").Change(name);
        form.Find("#login-password").Change(password);
        form.Find("form").Submit();
    }
}