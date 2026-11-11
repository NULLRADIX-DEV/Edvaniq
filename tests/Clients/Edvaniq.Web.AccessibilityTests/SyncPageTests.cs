using System.Text.Json;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #218 and A64: the server is the only source. What one device changes, another shows once it has the focus again,
// without a reload, and a third shows on opening. What the user typed or ticked survives the refresh, and the browser
// asks before a reload throws it away.
public sealed class SyncPageTests(WebShell shell)
{
    private const string Typed = "Mein Text, noch nicht gesendet.";

    [Fact]
    public async Task ChangeOnOneDevice_ShowsOnTheOtherAfterTheFocusComesBack()
    {
        var server = new SharedServer();
        await using var first = await shell.OpenSignedInAsync("/themen", WebShell.PhoneWidth, server.AnswerAsync);
        await using var second = await shell.OpenSignedInAsync("/themen", WebShell.DesktopWidth, server.AnswerAsync);
        await Assertions.Expect(second.Page.Locator(".material-status")).ToContainTextAsync("Du hast noch keinen Text eingefügt.");
        await second.Page.GetByLabel("Titel").FillAsync(Typed);
        await second.Page.EvaluateAsync("() => window.notReloaded = true");

        await first.Page.GetByLabel("Text", new PageGetByLabelOptions { Exact = true }).FillAsync("Die Photosynthese wandelt Licht um.");
        await first.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Themen erkennen" }).ClickAsync();
        await Assertions.Expect(first.Page.Locator(".topic-list")).ToContainTextAsync("Photosynthese", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        await Assertions.Expect(second.Page.Locator(".topic-list")).ToHaveCountAsync(0);

        await ReturnUntilAsync(second.Page, () => second.Page.Locator(".topic-list").CountAsync().ContinueWith(count => count.Result > 0));

        await Assertions.Expect(second.Page.Locator(".topic-list")).ToContainTextAsync("Photosynthese");
        await Assertions.Expect(second.Page.Locator(".material-status")).ToContainTextAsync("sind da");
        await Assertions.Expect(second.Page.GetByLabel("Titel")).ToHaveValueAsync(Typed);
        Assert.True(await second.Page.EvaluateAsync<bool>("() => window.notReloaded === true"), "The page was reloaded.");

        await using var third = await shell.OpenSignedInAsync("/themen", WebShell.DesktopWidth, server.AnswerAsync);
        await Assertions.Expect(third.Page.Locator(".topic-list")).ToContainTextAsync("Photosynthese");
    }

    [Fact]
    public async Task UnsentText_MakesTheBrowserAskBeforeLeaving()
    {
        var server = new SharedServer();
        await using var page = await shell.OpenSignedInAsync("/themen", WebShell.PhoneWidth, server.AnswerAsync);
        var text = page.Page.GetByLabel("Text", new PageGetByLabelOptions { Exact = true });
        await text.ClickAsync();
        await text.PressSequentiallyAsync("Halb getippt");

        var dialog = await AskedOnCloseAsync(page.Page);

        Assert.Equal(DialogType.BeforeUnload, dialog);
        Assert.False(page.Page.IsClosed);
        await Assertions.Expect(text).ToHaveValueAsync("Halb getippt");
    }

    [Fact]
    public async Task WithoutInput_TheBrowserLeavesWithoutAsking()
    {
        var server = new SharedServer();
        var page = await shell.OpenSignedInAsync("/themen", WebShell.PhoneWidth, server.AnswerAsync);
        await using var _ = page;
        var asked = false;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Page.Dialog += (_, dialog) => asked = true;
        page.Page.Close += (_, _) => closed.TrySetResult();
        await page.Page.Locator("#paste-text").ClickAsync();

        await page.Page.CloseAsync(new PageCloseOptions { RunBeforeUnload = true });

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.False(asked);
    }

    [Fact]
    public async Task OpenQuestion_StaysOnReturn_AndATickedAnswerMakesTheBrowserAsk()
    {
        await using var page = await shell.OpenSignedInAsync($"/themen/{Material.TopicId}/ueben", WebShell.PhoneWidth, browser => Material.AnswerQuizAsync(browser));
        await Assertions.Expect(page.Page.Locator("legend")).ToContainTextAsync("Wo findet die Lichtreaktion statt?");
        await page.Page.GetByLabel("In den Thylakoiden").CheckAsync();
        var topicCalls = TopicCalls(page);

        await ReturnUntilAsync(page.Page, () => Task.FromResult(TopicCalls(page) > topicCalls));

        Assert.Single(page.Requests, url => url.EndsWith("/api/assessment/attempts", StringComparison.Ordinal));
        await Assertions.Expect(page.Page.GetByLabel("In den Thylakoiden")).ToBeCheckedAsync();
        Assert.Equal(DialogType.BeforeUnload, await AskedOnCloseAsync(page.Page));
    }

    private static int TopicCalls(ShellPage page) =>
        page.Requests.Count(url => url.EndsWith("/api/knowledge/topics", StringComparison.Ordinal));

    // The window loses the focus and gets it back, like a switch to another app and back. The page listens only once
    // its script has loaded, and returns within two seconds count once, so a slow start gets another go.
    private static async Task ReturnUntilAsync(IPage page, Func<Task<bool>> done)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await page.EvaluateAsync("() => { window.dispatchEvent(new Event('blur')); window.dispatchEvent(new Event('focus')); }");
            for (var check = 0; check < 25; check++)
            {
                if (await done())
                {
                    return;
                }

                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        }

        Assert.Fail("The page did not refresh on return.");
    }

    // Closing runs beforeunload. A dialog asks, the test stays on the page.
    private static async Task<string> AskedOnCloseAsync(IPage page)
    {
        var asked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Dialog += async (_, dialog) =>
        {
            asked.TrySetResult(dialog.Type);
            await dialog.DismissAsync();
        };

        await page.CloseAsync(new PageCloseOptions { RunBeforeUnload = true });
        return await asked.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    }

    // Content and Knowledge as two devices of one user see them: one state for both, a paste is analyzed at once.
    private sealed class SharedServer
    {
        private readonly Lock sync = new();
        private object[] documents = [];
        private object[] topics = [];

        public async Task AnswerAsync(IPage page)
        {
            await page.RouteAsync("**/api/content/documents", route =>
            {
                if (route.Request.Method == "POST")
                {
                    lock (sync)
                    {
                        documents = [Material.Document("Ready")];
                        topics = Material.Topics;
                    }

                    return FulfillAsync(route, Material.Document("Uploaded"), 201);
                }

                lock (sync)
                {
                    return FulfillAsync(route, documents);
                }
            });
            await page.RouteAsync("**/api/knowledge/topics", route =>
            {
                lock (sync)
                {
                    return FulfillAsync(route, topics);
                }
            });
        }

        private static Task FulfillAsync(IRoute route, object body, int status = 200) =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = status,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
            });
    }
}