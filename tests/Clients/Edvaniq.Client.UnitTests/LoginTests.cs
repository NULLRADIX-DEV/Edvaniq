using Bunit;
using Bunit.TestDoubles;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Edvaniq.Client.UnitTests;

// The login page: name and password, a hint in plain German when it does not work, and back to where the user came from
// when it does.
public sealed class LoginTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private const string Password = "richtig-langes-passwort";

    private FakeWebSession session = new();

    private FakeAccountConfirmations confirmations = new();

    // What the fields show in the browser on submit. Null: the binding knows it all, as when somebody typed.
    private readonly JSRuntimeInvocationHandler<string?[]?> shown;

    public LoginTests()
    {
        Services.AddSingleton<IWebSession>(_ => session);
        Services.AddSingleton<IAccountConfirmations>(_ => confirmations);
        AddAuthorization();
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        JSInterop.Mode = JSRuntimeMode.Strict;
        shown = JSInterop.SetupModule(LoginForm.FormValuesModule)
            .Setup<string?[]?>("read", call => call.Arguments is [string[] ids] && ids.SequenceEqual(["login-name", "login-password"]));
        shown.SetResult(null);
    }

    // #1013: autofill or a password manager filled the fields, and the page heard nothing of it.
    [Fact]
    public void FieldsFilledWithoutAnEvent_SignIn()
    {
        var session = new FakeWebSession();
        var form = RenderForm(session);
        shown.SetResult(["ben", Password]);

        form.Find("form").Submit();

        Assert.Equal(("ben", Password), Assert.Single(session.Logins));
    }

    // What the user sees counts, also when the binding knows something else.
    [Fact]
    public void WhatTheFieldsShow_WinsOverTheBinding()
    {
        var session = new FakeWebSession();
        var form = RenderForm(session);
        form.Find("#login-name").Change("bne");
        form.Find("#login-password").Change("alt");
        shown.SetResult([" ben ", Password]);

        form.Find("form").Submit();

        Assert.Equal(("ben", Password), Assert.Single(session.Logins));
    }

    // Read first, then checked: fields that are empty in the browser still get the hint.
    [Fact]
    public void FieldsEmptyInTheBrowser_SendNothing()
    {
        var session = new FakeWebSession();
        var form = RenderForm(session);
        shown.SetResult(["", ""]);

        form.Find("form").Submit();

        Assert.Empty(session.Logins);
        Assert.Equal(FeedbackTexts.Login.Missing, form.Find("[role=alert] p").TextContent);
    }

    // Without the script the binding still works.
    [Fact]
    public void WithoutTheScript_TheBindingCounts()
    {
        var session = new FakeWebSession();
        var form = RenderForm(session);
        shown.SetException(new JSException("no module"));

        SignIn(form);

        Assert.Equal(("ben", Password), Assert.Single(session.Logins));
    }

    [Fact]
    public void Fields_HaveLabels_AndTellTheBrowserWhatTheyAre()
    {
        var form = RenderForm(new FakeWebSession());

        Assert.Equal("E-Mail oder Name", form.Find("label[for=login-name]").TextContent);
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
            "E-Mail, Name oder Passwort stimmen nicht. Prüf alles und versuch es noch einmal.",
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
        Assert.Equal(FeedbackTexts.Login.Missing, form.Find("[role=alert] p").TextContent);
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

    // #252: a locked login waits longer, said in minutes like any other wait.
    [Fact]
    public void Locked_SaysTheMinutes()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.TooManyAttempts, TimeSpan.FromMinutes(15))));

        SignIn(form, "kim@example.org");

        Assert.Equal("Zu viele Versuche. Warte 15 Minuten und versuch es dann noch einmal.", form.Find("[role=alert] p").TextContent);
    }

    [Fact]
    public void Unconfirmed_ShowsTheHostsSentence_AndOffersANewLink()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unconfirmed, Title: "Bestätige zuerst deine Adresse.")));

        SignIn(form, " kim@example.org ");

        var notice = form.Find("[role=alert] .login-unconfirmed");
        Assert.Equal("Bestätige zuerst deine Adresse.", notice.QuerySelector("p")!.TextContent);
        Assert.Equal(FeedbackTexts.Login.Resend, notice.QuerySelector("button")!.TextContent.Trim());
        Assert.Equal("button", notice.QuerySelector("button")!.GetAttribute("type"));
        Assert.Empty(form.FindAll(".login-hint"));
    }

    [Fact]
    public void Unconfirmed_WithoutATitle_HasItsOwnSentence()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unconfirmed)));

        SignIn(form, "kim@example.org");

        Assert.Equal(FeedbackTexts.Login.Unconfirmed, form.Find(".login-unconfirmed p").TextContent);
    }

    // A team name never waits for a confirmation, and it must not go out as an address.
    [Fact]
    public void Unconfirmed_ForAName_OffersNoLink()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unconfirmed)));

        SignIn(form, "ben");

        Assert.Empty(form.FindAll(".login-unconfirmed button"));
    }

    [Theory]
    [InlineData(false, "Wir haben dir einen neuen Link geschickt. Schau in dein Postfach, auch in den Spam-Ordner.")]
    [InlineData(true, "Wir haben dir einen neuen Link geschickt. Schau in dein Postfach, auch in den Spam-Ordner. Die Mail kann sich verspäten.")]
    public void NewLink_GoesToTheTypedAddress_AndSaysSo(bool delayed, string note)
    {
        var fake = new FakeAccountConfirmations(new ResendOutcome(ResendResult.Sent, delayed));
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unconfirmed)), confirmationsFake: fake);
        SignIn(form, " kim@example.org ");
        form.Find("#login-name").Change("jemand@anders.org");

        form.Find(".login-unconfirmed button").Click();

        Assert.Equal("kim@example.org", Assert.Single(fake.Resent));
        Assert.Equal(note, form.Find(".login-resend-note").TextContent);
        Assert.NotNull(form.Find("[role=alert] .login-resend-note"));
    }

    [Theory]
    [InlineData(420, "Du hast gerade schon einen neuen Link bekommen. Warte 7 Minuten und frag dann noch einmal.")]
    [InlineData(null, "Du hast gerade schon einen neuen Link bekommen. Warte 10 Minuten und frag dann noch einmal.")]
    public void NewLink_Limited_SaysHowLongToWait(int? seconds, string note)
    {
        var fake = new FakeAccountConfirmations(new ResendOutcome(
            ResendResult.Limited, RetryAfter: seconds is null ? null : TimeSpan.FromSeconds(seconds.Value)));
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unconfirmed)), confirmationsFake: fake);
        SignIn(form, "kim@example.org");

        form.Find(".login-unconfirmed button").Click();

        Assert.Equal(note, form.Find(".login-resend-note").TextContent);
    }

    [Fact]
    public void NewLink_Unavailable_SaysSo()
    {
        var fake = new FakeAccountConfirmations(new ResendOutcome(ResendResult.Unavailable));
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unconfirmed)), confirmationsFake: fake);
        SignIn(form, "kim@example.org");

        form.Find(".login-unconfirmed button").Click();

        Assert.Equal(FeedbackTexts.Login.ResendUnavailable, form.Find(".login-resend-note").TextContent);
    }

    // A new attempt starts over: the note of the last link goes.
    [Fact]
    public void NextAttempt_ClearsTheLinkNote()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.Unconfirmed)));
        SignIn(form, "kim@example.org");
        form.Find(".login-unconfirmed button").Click();

        SignIn(form, "kim@example.org");

        Assert.Empty(form.FindAll(".login-resend-note"));
    }

    // #269: the account of a minor waits for its approval. The hint looks unlike an error and names no request id.
    [Fact]
    public void AwaitingApproval_ShowsItsOwnHint_WithTheHostsSentence()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(
            SignInResult.AwaitingApproval, Title: "Deine Eltern müssen noch zustimmen.", TraceId: TraceId)));

        SignIn(form, "kim@example.org");

        var notice = form.Find("[role=alert] .login-approval");
        Assert.Contains("notice-info", notice.ClassList);
        Assert.Equal(FeedbackTexts.Login.AwaitingApprovalTitle, notice.QuerySelector(".notice-title")!.TextContent);
        Assert.Equal("Deine Eltern müssen noch zustimmen.", notice.QuerySelector(".notice-body > p:not(.notice-title)")!.TextContent);
        Assert.Empty(form.FindAll(".login-hint"));
        Assert.Empty(form.FindAll(".login-unconfirmed"));
        Assert.Empty(form.FindAll(".request-id"));
        Assert.Empty(form.FindAll("[role=alert] button"));
    }

    [Fact]
    public void AwaitingApproval_WithoutATitle_HasAChildFriendlySentence()
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(SignInResult.AwaitingApproval)));

        SignIn(form, "kim@example.org");

        Assert.Equal(FeedbackTexts.Login.AwaitingApproval, form.Find(".login-approval .notice-body > p:not(.notice-title)").TextContent);
    }

    // What needs a step of its own looks unlike an error: a warning with the link, a note for the approval.
    [Theory]
    [InlineData(SignInResult.WrongCredentials, "login-hint")]
    [InlineData(SignInResult.TooManyAttempts, "login-hint")]
    [InlineData(SignInResult.Unavailable, "login-hint")]
    [InlineData(SignInResult.Unconfirmed, "notice notice-warning login-unconfirmed")]
    [InlineData(SignInResult.AwaitingApproval, "notice notice-info login-approval")]
    public void EachRefusal_HasItsLook(SignInResult result, string look)
    {
        var form = RenderForm(new FakeWebSession(new SignInOutcome(result, TimeSpan.FromMinutes(1))));

        SignIn(form, "kim@example.org");

        Assert.Equal(look, Assert.Single(form.FindAll("[role=alert] > *")).ClassName);
    }

    private IRenderedComponent<LoginForm> RenderForm(FakeWebSession fake, string? returnUrl = null, FakeAccountConfirmations? confirmationsFake = null)
    {
        session = fake;
        confirmations = confirmationsFake ?? new FakeAccountConfirmations();
        return Render<LoginForm>(parameters => parameters.Add(form => form.ReturnTo, returnUrl));
    }

    private static void SignIn(IRenderedComponent<IComponent> form, string name = "ben", string password = "richtig-langes-passwort")
    {
        form.Find("#login-name").Change(name);
        form.Find("#login-password").Change(password);
        form.Find("form").Submit();
    }
}