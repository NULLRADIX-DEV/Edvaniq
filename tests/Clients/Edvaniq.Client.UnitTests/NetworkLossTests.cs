using System.Text.RegularExpressions;

using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Edvaniq.Client.UnitTests;

// #196: a lost connection or a failing server shows a plain hint with a way to try again, never an empty page, and
// never anything technical but the trace id.
public sealed partial class NetworkLossTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Attempt = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");

    private static readonly AskedQuestion Question = new(
        Attempt, Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden"], 1);

    private static readonly GradedAnswer Graded = new(Attempt, 1, true, 1, "Die Lichtreaktion läuft an den Thylakoiden ab.", 3000);

    private readonly FakeReturnSignal signal = new();
    private readonly ManualTime time = new();

    public NetworkLossTests()
    {
        Services.AddQuietAnswerSync();
        Services.AddSingleton<IReturnSignal>(signal);
        Services.AddSingleton<TimeProvider>(time);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // If the browser app never loads, nothing would replace "Wird geladen …". The page says from the start what helps,
    // with a plain link that works without the runtime and fetches the page whole.
    [Fact]
    public void WhilePrerendering_TheLoadingHint_SaysWhatHelps_WithAPlainReloadLink()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/themen");

        var page = Render<PageLoading>();

        var status = page.Find("[role=status]");
        Assert.Equal("Wird geladen …", status.QuerySelector(".loading")!.TextContent);
        Assert.Equal(
            "Bleibt das so, prüf deine Internetverbindung und lade die Seite neu.",
            Squeeze(status.QuerySelector(".page-loading-help")!.TextContent));
        var reload = status.QuerySelector(".page-loading-help a")!;
        Assert.Equal("http://localhost/themen", reload.GetAttribute("href"));
        Assert.Equal("false", reload.GetAttribute("data-enhance-nav"));
    }

    // Once the app runs it says itself when something fails, the extra line would only be noise.
    [Fact]
    public void Interactive_TheLoadingHint_IsJustLoading()
    {
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var page = Render<PageLoading>();

        Assert.Empty(page.FindAll(".page-loading-help"));
        Assert.Equal("Wird geladen …", page.Find("[role=status]").TextContent.Trim());
    }

    // AC2: back online, or back in the tab, the shown hint checks once more by itself and goes away.
    [Fact]
    public void Banner_ChecksAgainOnReturn_AndGoesAwayWhenTheServerIsBack()
    {
        var backend = new FakeBackendStatus(false, true);
        Services.AddSingleton<IBackendStatus>(backend);
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        var banner = Render<BackendStatusBanner>();
        Assert.Contains("Keine Verbindung zu Edvaniq.", banner.Find("[role=status]").TextContent);

        signal.Return();

        banner.WaitForAssertion(() => Assert.Empty(banner.FindAll("button")));
        Assert.Equal(2, backend.Checks);
        Assert.Equal("Die Verbindung zum Server ist wieder da.", banner.Find("[role=status] .visually-hidden").TextContent);
    }

    // Nothing to fix, nothing to ask: no request on each return while everything works.
    [Fact]
    public void Banner_WithoutHint_AsksNothingOnReturn()
    {
        var backend = new FakeBackendStatus(true);
        Services.AddSingleton<IBackendStatus>(backend);
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        Render<BackendStatusBanner>();

        signal.Return();

        Assert.Equal(1, backend.Checks);
    }

    // AC2: a question that didn't load comes by itself once the connection is back.
    [Fact]
    public void Quiz_ThatFailedToLoad_AsksAgainOnReturn()
    {
        var quiz = new FakeQuiz().Ask(CallOutcome<AskedQuestion>.Failed(TraceId)).Ask(Question);
        var page = RenderQuiz(quiz);
        Assert.Contains("Die Frage ließ sich gerade nicht laden.", page.Find("[role=status]").TextContent);

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal("Wo findet die Lichtreaktion statt?", page.Find("legend").TextContent));
        Assert.Equal(2, quiz.Asked.Count);
    }

    // AC3: an answer lost on its way is sent again only by the user, to the same attempt. The server answers that with
    // the stored grade, so it counts once (Assessment, SameAnswerSentTwice_CountsOnce). A return asks no new question:
    // that would throw the attempt away.
    [Fact]
    public void LostAnswer_StaysWithItsAttempt_UntilTheUserSendsItAgain()
    {
        var quiz = new FakeQuiz().Ask(Question).Answer(CallOutcome<GradedAnswer>.Failed(TraceId)).Answer(Graded);
        var page = RenderQuiz(quiz);
        time.Advance(TimeSpan.FromSeconds(3));
        page.Find("#option-1").Change("1");
        page.Find("form").Submit();

        var alert = page.Find("form [role=alert]").TextContent;
        Assert.Contains("Deine Antwort ist nicht angekommen.", alert);
        Assert.Contains("zählt sie trotzdem nur einmal", alert);
        Assert.Contains(TraceId, alert);

        signal.Return();

        Assert.Single(quiz.Asked);
        Assert.Single(quiz.Answered);
        Assert.True(page.Find("#option-1").HasAttribute("checked"));

        page.Find("form").Submit();

        Assert.Equal([(Attempt, 1, 3000), (Attempt, 1, 3000)], quiz.Answered);
        Assert.NotNull(page.Find(".quiz-feedback"));
    }

    // AC4: every hint for a failed call, with a trace id at hand. Only that id may show, nothing else technical.
    [Fact]
    public void FailureHints_NameNothingTechnical()
    {
        Services.AddSingleton<IBackendStatus>(new FakeBackendStatus(false));
        Services.AddSingleton<IPing>(new FakePing(PingOutcome.Failed(TraceId)));
        Services.AddSingleton<IQuiz>(new FakeQuiz().Ask(Question).Answer(CallOutcome<GradedAnswer>.Failed(TraceId)));
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial()
            .Documents(CallOutcome<IReadOnlyList<DocumentInfo>>.Failed(TraceId))
            .Paste(CallOutcome<DocumentInfo>.Failed(TraceId)));
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));

        var banner = Render<BackendStatusBanner>().Markup;
        var ping = Render<SystemStatus>().Find("[role=status]").TextContent;
        var topics = Render<Topics>();
        var loadFailed = topics.Find("[role=status]").TextContent;
        topics.Confirm();
        topics.Find("#paste-text").Change("Mein Text");
        topics.Find("form").Submit();
        var pasteFailed = topics.Find("form [role=alert]").TextContent;
        var quiz = Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));
        quiz.Find("#option-1").Change("1");
        quiz.Find("form").Submit();
        var answerFailed = quiz.Find("form [role=alert]").TextContent;
        var serverError = Text(Render<ServerError>(parameters => parameters
            .Add(p => p.TraceId, TraceId)
            .Add(p => p.RetryHref, "/themen")).Markup);

        string[] hints = [Text(banner), ping, loadFailed, pasteFailed, answerFailed, serverError, PrerenderedLoading()];

        Assert.All(hints, hint => Assert.Empty(TechnicalDetails(hint.Replace(TraceId, "", StringComparison.Ordinal))));
        Assert.Contains("Keine Verbindung zu Edvaniq.", hints[0]);
        Assert.All(hints[1..6], hint => Assert.Contains(TraceId, hint));
    }

    // The check itself: it catches status codes, exceptions and addresses, but leaves numbers of a normal text alone.
    [Theory]
    [InlineData("Fehler 503")]
    [InlineData("Der Server sagt (404).")]
    [InlineData("Status: 500")]
    [InlineData("Service Unavailable")]
    [InlineData("Bad Gateway")]
    [InlineData("System.Net.Http.HttpRequestException: No connection")]
    [InlineData("TaskCanceledException")]
    [InlineData("   at Edvaniq.Client.Core.HostCall.GuardAsync()")]
    [InlineData("https://edvaniq.example/api/status")]
    [InlineData("Ruf /api/assessment/attempts auf")]
    [InlineData("http://localhost:5000")]
    [InlineData("Ziel 10.210.5.3 nicht erreichbar")]
    public void TechnicalDetails_AreFound(string text) => Assert.NotEmpty(TechnicalDetails(text));

    [Theory]
    [InlineData("Erlaubt sind höchstens 200.000 Zeichen.")]
    [InlineData("Der Titel ist zu lang. Erlaubt sind höchstens 80 Zeichen.")]
    [InlineData("Ping Nr. 42 am 11.11.2026 um 09:00:00")]
    [InlineData("Versuch es in ein paar Sekunden noch einmal.")]
    [InlineData("Seite 1.500 von 2.000")]
    [InlineData("Anfragekennung: ")]
    public void NormalText_HasNoTechnicalDetails(string text) => Assert.Empty(TechnicalDetails(text));

    private static List<string> TechnicalDetails(string text) =>
    [
        .. StatusCode().Matches(text).Select(match => match.Value),
        .. ReasonPhrase().Matches(text).Select(match => match.Value),
        .. Inside().Matches(text).Select(match => match.Value),
        .. Address().Matches(text).Select(match => match.Value),
    ];

    // An error code alone, not part of a longer number like 200.000 or of a word like the trace id.
    [GeneratedRegex(@"(?<![\w.,:])[45]\d\d(?![\w.,]?\d)")]
    private static partial Regex StatusCode();

    [GeneratedRegex("Bad Request|Unauthorized|Forbidden|Not Found|Conflict|Internal Server Error|Bad Gateway|Service Unavailable|Gateway Timeout", RegexOptions.IgnoreCase)]
    private static partial Regex ReasonPhrase();

    [GeneratedRegex(@"Exception|Stack ?trace|\bat [A-Z]\w*\.|\bSystem\.", RegexOptions.IgnoreCase)]
    private static partial Regex Inside();

    [GeneratedRegex(@"https?://|localhost|/api/|\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    private string PrerenderedLoading()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        var text = Render<PageLoading>().Find("[role=status]").TextContent;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return text;
    }

    private IRenderedComponent<Practice> RenderQuiz(FakeQuiz quiz)
    {
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Topics(new TopicInfo(Topic, "Photosynthese", [])));
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
        return Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));
    }

    private static string Text(string markup) => Squeeze(TagOrScript().Replace(markup, " "));

    private static string Squeeze(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagOrScript();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}