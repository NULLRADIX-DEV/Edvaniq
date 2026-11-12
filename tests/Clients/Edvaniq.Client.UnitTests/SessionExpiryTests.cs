using System.Net;

using Bunit;
using Bunit.TestDoubles;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// The login runs out while someone works (#197): one hint in the shell with the way to the login and back, and what was
// not sent yet waits there, for the same account only.
public sealed class SessionExpiryTests : BunitContext
{
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Attempt = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");

    private static readonly AskedQuestion Question = new(
        Attempt, Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden", "Im Cytoplasma"], 1);

    private readonly FakeWebSession session = new() { SignedIn = true };
    private readonly SessionWatch watch = new();
    private readonly Drafts drafts = new();
    private readonly FakeLearningMaterial material = new();
    private readonly FakeQuiz quiz = new();
    private readonly SessionAuthenticationState state;
    private readonly BunitAuthorizationContext authorization;

    public SessionExpiryTests()
    {
        Services.AddSingleton(watch);
        Services.AddSingleton(drafts);
        Services.AddSingleton<IWebSession>(session);
        Services.AddSingleton<TimeProvider>(new ManualTime());
        Services.AddSingleton<ILearningMaterial>(material);
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddQuietAnswerSync();
        authorization = AddAuthorization();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        state = new SessionAuthenticationState(session, drafts, watch);
    }

    [Theory]
    [InlineData("api/content/documents", HttpStatusCode.Unauthorized, true)]
    [InlineData("api/assessment/attempts", HttpStatusCode.Unauthorized, true)]
    [InlineData("api/content/documents", HttpStatusCode.OK, false)]
    [InlineData("api/content/documents", HttpStatusCode.Forbidden, false)]
    [InlineData("session/user", HttpStatusCode.Unauthorized, false)]
    [InlineData("session/login", HttpStatusCode.Unauthorized, false)]
    public async Task OnlyA401UnderApi_EndsTheLogin(string path, HttpStatusCode status, bool ended)
    {
        var seen = new SessionWatch();
        using var client = new HttpClient(new SessionWatchHandler(seen) { InnerHandler = new Answer(status) })
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
        };

        using var _ = await client.GetAsync(path, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(ended, seen.Ended);
    }

    [Fact]
    public async Task Shell_SaysTheLoginRanOut_AndLeadsToTheLogin_AndBackHere()
    {
        await state.GetAuthenticationStateAsync();
        authorization.SetAuthorized("ben");
        Services.GetRequiredService<NavigationManager>().NavigateTo("themen/123/ueben?x=1");
        var notice = Render<SessionEndedNotice>();
        Assert.Empty(notice.FindAll(".notice"));

        watch.Report();

        notice.WaitForAssertion(() => Assert.Contains(FeedbackTexts.Session.Ended, notice.Find("[role=alert] .notice").TextContent));
        Assert.Equal(
            "login?returnUrl=" + Uri.EscapeDataString("/themen/123/ueben?x=1"),
            notice.Find("[role=alert] a").GetAttribute("href"));
    }

    // Nobody was signed in, so nothing ran out. And the login page says it on its own.
    [Fact]
    public void Shell_SaysNothing_WithoutALogin_OrOnTheLoginPage()
    {
        watch.Report();

        var signedOut = Render<SessionEndedNotice>();
        Assert.Empty(signedOut.FindAll(".notice"));

        authorization.SetAuthorized("ben");
        Services.GetRequiredService<NavigationManager>().NavigateTo("login?returnUrl=%2Fthemen");
        var onLogin = Render<SessionEndedNotice>();
        Assert.Empty(onLogin.FindAll(".notice"));

        var login = Render<Login>();
        Assert.Contains(FeedbackTexts.Session.Ended, login.Find(".login-ended").TextContent);
    }

    [Fact]
    public async Task NewLogin_EndsTheHint()
    {
        await state.GetAuthenticationStateAsync();
        watch.Report();

        await state.Refresh();

        Assert.False(watch.Ended);
    }

    [Fact]
    public async Task PastedText_WaitsThroughTheLogin_ForTheSameAccount()
    {
        await state.GetAuthenticationStateAsync();
        material.Paste(CallOutcome<DocumentInfo>.SignedOut)
            .Paste(CallOutcome<DocumentInfo>.Ok(new DocumentInfo(Guid.NewGuid(), "Fotosynthese", DocumentStatus.Uploaded, DateTimeOffset.UnixEpoch)));
        var before = Render<MaterialView>();

        before.Confirm();
        before.Find("#paste-title").Input("Fotosynthese");
        before.Find("#paste-text").Change("Die Lichtreaktion läuft in den Thylakoiden ab.");
        before.Find("form").Submit();
        Assert.Contains(FeedbackTexts.Session.TextKept, before.Find("[role=status]").TextContent);

        await LeaveForTheLoginAsync(FakeWebSession.DefaultAccount);
        var after = Render<MaterialView>();

        Assert.Equal("Fotosynthese", after.Find("#paste-title").GetAttribute("value"));
        Assert.Equal("Die Lichtreaktion läuft in den Thylakoiden ab.", after.Find("#paste-text").GetAttribute("value"));
        Assert.Contains(FeedbackTexts.Session.TextBack, after.Find(".paste-form [role=alert]").TextContent);

        // Never sent by itself: only the user's own click sends it, once.
        Assert.Single(material.Pasted);
        // The tick isn't kept with the draft, the user confirms again.
        after.Confirm();
        after.Find("form").Submit();
        Assert.Equal(2, material.Pasted.Count);
    }

    // A shared device: what one account typed never shows up for another.
    [Fact]
    public async Task PastedText_OfOneAccount_IsGone_ForAnother()
    {
        await state.GetAuthenticationStateAsync();
        material.Paste(CallOutcome<DocumentInfo>.SignedOut);
        var before = Render<MaterialView>();
        before.Find("#paste-text").Change("Text von A");
        before.Find("form").Submit();

        await LeaveForTheLoginAsync("konto-b");
        var after = Render<MaterialView>();

        Assert.Equal("", after.Find("#paste-text").GetAttribute("value") ?? "");
        Assert.DoesNotContain("Text von A", after.Markup);
        Assert.Empty(after.FindAll(".paste-form [role=alert] p"));
    }

    [Fact]
    public async Task Logout_ThrowsTheDraftsAway()
    {
        await state.GetAuthenticationStateAsync();
        drafts.Keep("x", "Entwurf");

        await session.SignOutAsync(Xunit.TestContext.Current.CancellationToken);
        await state.Refresh();

        Assert.Null(drafts.Take<string>("x"));
    }

    // The answer that met the run-out login never reached Assessment. After the login the same attempt is back, nothing is
    // sent by itself, and the user's check goes to that attempt, which Assessment grades once
    // (AttemptTests.SameAnswerSentTwice_CountsOnce).
    [Fact]
    public async Task OpenAnswer_GoesToTheSameAttempt_AfterTheLogin()
    {
        await state.GetAuthenticationStateAsync();
        quiz.Ask(Question).Answer(CallOutcome<GradedAnswer>.SignedOut).Answer(new GradedAnswer(Attempt, 1, true, 1, "Erklärung", 1500));
        var before = Render<QuizView>(parameters => parameters.Add(p => p.TopicId, Topic));

        before.Find("#option-1").Change("1");
        before.Find("form").Submit();
        Assert.Contains(FeedbackTexts.Session.AnswerKept, before.Find(".quiz-form [role=alert]").TextContent);

        await LeaveForTheLoginAsync(FakeWebSession.DefaultAccount);
        var after = Render<QuizView>(parameters => parameters.Add(p => p.TopicId, Topic));

        Assert.Single(quiz.Asked);
        Assert.Equal("Wo findet die Lichtreaktion statt?", after.Find("legend").TextContent);
        Assert.True(after.Find("#option-1").HasAttribute("checked"));
        Assert.Contains(FeedbackTexts.Session.AnswerBack, after.Find(".quiz-form [role=alert]").TextContent);
        Assert.Single(quiz.Answered);

        after.Find("form").Submit();

        Assert.Equal([Attempt, Attempt], quiz.Answered.Select(answer => answer.AttemptId));
        Assert.Equal(quiz.Answered[0].ResponseTimeMs, quiz.Answered[1].ResponseTimeMs);
        Assert.NotNull(after.Find("#feedback-heading"));
    }

    [Fact]
    public async Task OpenAnswer_OfOneAccount_IsGone_ForAnother()
    {
        await state.GetAuthenticationStateAsync();
        quiz.Ask(Question).Answer(CallOutcome<GradedAnswer>.SignedOut).Answer(new GradedAnswer(Attempt, 1, true, 1, "Erklärung", 1500));
        var before = Render<QuizView>(parameters => parameters.Add(p => p.TopicId, Topic));
        before.Find("#option-1").Change("1");
        before.Find("form").Submit();

        await LeaveForTheLoginAsync("konto-b");
        Render<QuizView>(parameters => parameters.Add(p => p.TopicId, Topic));

        Assert.Equal(2, quiz.Asked.Count);
    }

    // The page goes away with the way to the login, then someone signs in as the given account.
    private async Task LeaveForTheLoginAsync(string account)
    {
        await DisposeComponentsAsync();
        session.NextAccount = account;
        await session.SignInAsync("ben", "passwort", Xunit.TestContext.Current.CancellationToken);
        await state.Refresh();
    }

    private sealed class Answer(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}