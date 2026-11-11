using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Edvaniq.Client.UnitTests;

// #218: back on the page, it fetches the server's state anew, quietly and without touching what the user typed or ticked.
public sealed class ReturnTests : BunitContext
{
    private static readonly Guid Newest = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly DateTimeOffset Created = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly TopicInfo NewTopic = new(Guid.NewGuid(), "Photosynthese", [], Newest);

    private static readonly AskedQuestion Question = new(
        Guid.NewGuid(), Topic, Guid.NewGuid(), "Wo findet die Lichtreaktion statt?", ["Im Zellkern", "In den Thylakoiden"], 1);

    private readonly FakeReturnSignal signal = new();
    private readonly ManualTime time = new();

    public ReturnTests()
    {
        Services.AddSingleton<IReturnSignal>(signal);
        Services.AddSingleton<TimeProvider>(time);
    }

    private void Interactive()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    // AC1: what another device added shows up on return, without a loading hint in between.
    [Fact]
    public void Topics_ComeOnReturn()
    {
        var material = new FakeLearningMaterial()
            .Documents([])
            .Documents(new DocumentInfo(Newest, "Skript Bio", DocumentStatus.Ready, Created))
            .Topics()
            .Topics(NewTopic);
        var page = RenderTopics(material);
        Assert.Contains("Du hast noch keinen Text eingefügt.", page.Find("[role=status]").TextContent);

        signal.Return();

        page.WaitForAssertion(() => Assert.Contains("Photosynthese", page.Find(".topic-list").TextContent));
        Assert.Contains("sind da", page.Find("[role=status]").TextContent);
        Assert.Equal(2, material.DocumentCalls);
    }

    // AC3: the return leaves the form alone, and the browser asks before a reload throws the text away.
    [Fact]
    public void Topics_KeepWhatWasTyped()
    {
        var page = RenderTopics(new FakeLearningMaterial().Documents([]).Topics());
        page.Find("#paste-title").Input("Mitschrift");
        page.Find("#paste-text").Change("Die Photosynthese wandelt Licht in Zucker um.");

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal(1, JSInterop.Invocations.Count(call => call.Identifier.EndsWith("enableNavigationPrompt", StringComparison.Ordinal))));
        Assert.Equal("Mitschrift", page.Find("#paste-title").GetAttribute("value"));
        Assert.Equal("Die Photosynthese wandelt Licht in Zucker um.", page.Find("#paste-text").GetAttribute("value"));
    }

    // Without anything typed a reload goes ahead without asking.
    [Fact]
    public void Topics_WithoutInput_DoNotAsk()
    {
        RenderTopics(new FakeLearningMaterial().Documents([]).Topics());

        Assert.DoesNotContain(JSInterop.Invocations, call => call.Identifier.EndsWith("enableNavigationPrompt", StringComparison.Ordinal));
    }

    // A login that ran out keeps the form and its text, and a login in another tab counts on return.
    [Fact]
    public void Topics_SignedOut_KeepTheText_AndComeBackAfterALoginElsewhere()
    {
        var material = new FakeLearningMaterial()
            .Documents(CallOutcome<IReadOnlyList<DocumentInfo>>.SignedOut)
            .Documents([])
            .Topics();
        var page = RenderTopics(material);
        page.Find("#paste-text").Change("Noch nicht gesendet.");
        Assert.Contains(FeedbackTexts.Session.TextKept, page.Find("[role=status]").TextContent);

        signal.Return();

        page.WaitForAssertion(() => Assert.Contains("Du hast noch keinen Text eingefügt.", page.Find("[role=status]").TextContent));
        Assert.Equal("Noch nicht gesendet.", page.Find("#paste-text").GetAttribute("value"));
    }

    // AC1 on the quiz: an open attempt stays, with what is ticked. A new question on each return would be wrong.
    [Fact]
    public void Quiz_KeepsTheOpenAttempt()
    {
        var quiz = new FakeQuiz().Ask(Question).Ask(Question with { Id = Guid.NewGuid(), Text = "Neue Frage" });
        var material = new FakeLearningMaterial()
            .Topics(new TopicInfo(Topic, "Photosynthese", []))
            .Topics(new TopicInfo(Topic, "Fotosynthese", []));
        var page = RenderQuiz(quiz, material);
        page.Find("#option-1").Change("1");

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal("Frage zu „Fotosynthese“", page.Find("h1").TextContent));
        Assert.Single(quiz.Asked);
        Assert.Equal("Wo findet die Lichtreaktion statt?", page.Find("legend").TextContent);
        Assert.True(page.Find("#option-1").HasAttribute("checked"));
        Assert.Contains(JSInterop.Invocations, call => call.Identifier.EndsWith("enableNavigationPrompt", StringComparison.Ordinal));
    }

    // While there were no questions yet, the return asks again, and the focus moves to the question that came.
    [Fact]
    public void Quiz_NotReady_AsksAgainOnReturn()
    {
        var quiz = new FakeQuiz().Ask(new CallOutcome<AskedQuestion>(CallResult.NotReady)).Ask(Question);
        var page = RenderQuiz(quiz, new FakeLearningMaterial().Topics(new TopicInfo(Topic, "Photosynthese", [])));
        Assert.Contains("noch keine Fragen", page.Find("[role=status]").TextContent);

        signal.Return();

        page.WaitForAssertion(() => Assert.Equal("Wo findet die Lichtreaktion statt?", page.Find("legend").TextContent));
        Assert.Equal(2, quiz.Asked.Count);
        var focus = JSInterop.Invocations.Last(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal));
        Assert.Equal(page.Find("legend").GetAttribute("blazor:elementReference"), Assert.IsType<ElementReference>(focus.Arguments[0]).Id);
    }

    // Returns in quick succession, like alt-tabbing back and forth, count once.
    [Fact]
    public void QuickReturns_CountOnce()
    {
        var returns = 0;
        Interactive();
        Render<RefreshOnReturn>(parameters => parameters.Add(p => p.OnReturn, () => returns++));

        signal.Return();
        signal.Return();
        time.Advance(RefreshOnReturn.Quiet - TimeSpan.FromMilliseconds(1));
        signal.Return();
        time.Advance(TimeSpan.FromMilliseconds(1));
        signal.Return();

        Assert.Equal(2, returns);
    }

    [Fact]
    public async Task LeavingThePage_EndsTheSubscription()
    {
        Interactive();
        var component = Render<RefreshOnReturn>();
        Assert.Equal(1, signal.Subscribers);

        await component.Instance.DisposeAsync();

        Assert.Equal(0, signal.Subscribers);
    }

    // AC3: the script tells about typed text before the binding hears of it, and the page asks before a reload. Leaving
    // the page stops the script and releases the reference to .NET.
    [Fact]
    public async Task TypingWatch_TellsBeforeTheBinding_AndCleansUp()
    {
        Services.AddSingleton<ILearningMaterial>(new FakeLearningMaterial().Documents([]).Topics());
        Interactive();
        var module = JSInterop.SetupModule(TypingWatch.Module);
        var watch = module.SetupModule("watch", _ => true);
        watch.SetupVoid("stop").SetVoidResult();
        var page = Render<Topics>();
        var self = Assert.IsType<DotNetObjectReference<TypingWatch>>(module.VerifyInvoke("watch").Arguments[1]);
        Assert.Equal("paste-text", module.VerifyInvoke("watch").Arguments[0]);
        Assert.DoesNotContain(JSInterop.Invocations, call => call.Identifier.EndsWith("enableNavigationPrompt", StringComparison.Ordinal));

        await page.InvokeAsync(() => self.Value.Filled(true));

        page.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, call => call.Identifier.EndsWith("enableNavigationPrompt", StringComparison.Ordinal)));
        await DisposeComponentsAsync();
        Assert.Single(watch.Invocations["stop"]);
        Assert.Throws<ObjectDisposedException>(() => self.Value);
    }

    // The script starts with the first subscription and stops with the last, and the reference to .NET is released.
    [Fact]
    public async Task BrowserSignal_StartsOnce_AndCleansUp()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule(BrowserReturnSignal.Module);
        module.SetupVoid("start", _ => true).SetVoidResult();
        module.SetupVoid("stop").SetVoidResult();
        var browser = new BrowserReturnSignal(JSInterop.JSRuntime);
        var calls = 0;

        var first = await browser.SubscribeAsync(() => calls++);
        var second = await browser.SubscribeAsync(() => calls++);
        browser.Returned();
        var self = Assert.IsType<DotNetObjectReference<BrowserReturnSignal>>(module.VerifyInvoke("start").Arguments[0]);
        await first.DisposeAsync();
        await first.DisposeAsync();
        Assert.Empty(module.Invocations["stop"]);
        await second.DisposeAsync();

        Assert.Equal(2, calls);
        Assert.Single(JSInterop.Invocations, call => call.Identifier == "import");
        Assert.Single(module.Invocations["start"]);
        Assert.Single(module.Invocations["stop"]);
        Assert.Throws<ObjectDisposedException>(() => self.Value);
    }

    private IRenderedComponent<Topics> RenderTopics(FakeLearningMaterial material)
    {
        Services.AddSingleton<ILearningMaterial>(material);
        Interactive();
        return Render<Topics>();
    }

    private IRenderedComponent<Practice> RenderQuiz(FakeQuiz quiz, FakeLearningMaterial material)
    {
        Services.AddSingleton<IQuiz>(quiz);
        Services.AddSingleton<ILearningMaterial>(material);
        Interactive();
        return Render<Practice>(parameters => parameters.Add(p => p.TopicId, Topic));
    }
}