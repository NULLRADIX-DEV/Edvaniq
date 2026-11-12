using System.Text.Json;

using Edvaniq.Client.TestData;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// Markup in text from the user or the AI, in the real browser: it shows as the characters it is, nothing of it runs,
// no element of it turns up and the browser logs no error because of it.
public sealed class PlainTextPageTests(WebShell shell)
{
    private static readonly Guid AttemptId = Guid.Parse("8c9d0e1f-2a3b-4c4d-9e5f-6a7b8c9d0e1f");

    private static string All => string.Join(" ", HostileText.Payloads);

    [Fact]
    public async Task TopicsPage_ShowsMarkupAsText()
    {
        var watch = new Watch();
        await using var page = await shell.OpenSignedInAsync("/themen", WebShell.DesktopWidth, prepare: async browser =>
        {
            watch.On(browser);
            object[] topics = [.. HostileText.Payloads.Select((text, index) => new
            {
                id = Id(index),
                learningGoalId = Guid.Empty,
                name = text,
                documentId = Material.DocumentId,
                position = index,
                subtopics = new[] { new { id = Id(100 + index), name = text, documentId = Material.DocumentId, position = 100 + index } },
            })];
            await Material.AnswerAsync(browser, documents: [new[] { Material.Document("Failed", All, title: All) }], topics: [topics]);
        }, reloading: watch.Reset);

        var list = page.Page.Locator(".topic-list");
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync($"Die Analyse von „{All}“ hat nicht geklappt.");
        foreach (var text in HostileText.Payloads)
        {
            await Assertions.Expect(list).ToContainTextAsync(text);
        }

        await AssertNothingRanAsync(page, watch, ".material-status .notice-body, .topic-list");
    }

    [Fact]
    public async Task QuizPage_ShowsMarkupAsText()
    {
        var watch = new Watch();
        await using var page = await shell.OpenSignedInAsync($"/themen/{Material.TopicId}/ueben", WebShell.DesktopWidth, prepare: async browser =>
        {
            watch.On(browser);
            await Material.AnswerAsync(browser, documents: [Array.Empty<object>()], topics:
                [new[] { new { id = Material.TopicId, learningGoalId = Guid.Empty, name = All, documentId = Material.DocumentId, position = 0, subtopics = Array.Empty<object>() } }]);
            await browser.RouteAsync("**/api/assessment/attempts", route => FulfillAsync(route, new
            {
                id = AttemptId,
                topicId = Material.TopicId,
                questionId = Guid.NewGuid(),
                text = All,
                options = HostileText.Payloads,
                difficulty = 1,
            }, 201));
            await browser.RouteAsync("**/api/assessment/attempts/*/answer", route => FulfillAsync(route, new
            {
                id = AttemptId,
                topicId = Material.TopicId,
                questionId = Guid.NewGuid(),
                option = 0,
                correct = false,
                answer = 1,
                explanation = All,
                responseTimeMs = 1000,
                answeredAt = DateTime.UtcNow,
            }));
        }, reloading: watch.Reset);

        await Assertions.Expect(page.Page.Locator("legend")).ToHaveTextAsync(All);
        await Assertions.Expect(page.Page.Locator("h1")).ToHaveTextAsync($"Frage zu „{All}“");
        await page.Page.Locator("label[for=option-0]").ClickAsync();
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" }).ClickAsync();

        var feedback = page.Page.Locator(".quiz-feedback");
        await Assertions.Expect(feedback).ToContainTextAsync($"Richtige Antwort: {HostileText.Payloads[1]}");
        await Assertions.Expect(feedback.Locator(".quiz-explanation .markdown")).ToHaveTextAsync(All);
        Assert.Equal(HostileText.Payloads, await page.Page.Locator(".quiz-option-text").AllTextContentsAsync());

        await AssertNothingRanAsync(page, watch, "h1, .quiz-form, .quiz-feedback");
    }

    // Markdown that would run script as a link, an image, raw HTML or attributes: question, options and explanation show
    // it harmless, and the only links left go to the web or to mail.
    [Fact]
    public async Task QuizPage_RunsNoMarkdownAttack()
    {
        var watch = new Watch();
        var explanation = string.Join("\n\n", HostileText.MarkdownPayloads);
        await using var page = await shell.OpenSignedInAsync($"/themen/{Material.TopicId}/ueben", WebShell.DesktopWidth, prepare: async browser =>
        {
            watch.On(browser);
            await Material.AnswerAsync(browser, documents: [Array.Empty<object>()], topics:
                [new[] { new { id = Material.TopicId, learningGoalId = Guid.Empty, name = "Säuren", documentId = Material.DocumentId, position = 0, subtopics = Array.Empty<object>() } }]);
            await browser.RouteAsync("**/api/assessment/attempts", route => FulfillAsync(route, new
            {
                id = AttemptId,
                topicId = Material.TopicId,
                questionId = Guid.NewGuid(),
                text = explanation,
                options = HostileText.MarkdownPayloads,
                difficulty = 1,
            }, 201));
            await browser.RouteAsync("**/api/assessment/attempts/*/answer", route => FulfillAsync(route, new
            {
                id = AttemptId,
                topicId = Material.TopicId,
                questionId = Guid.NewGuid(),
                option = 0,
                correct = false,
                answer = 1,
                explanation,
                responseTimeMs = 1000,
                answeredAt = DateTime.UtcNow,
            }));
        }, reloading: watch.Reset);

        await Assertions.Expect(page.Page.Locator("legend")).ToContainTextAsync("Hier klicken");
        await page.Page.Locator("label[for=option-0]").ClickAsync();
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" }).ClickAsync();

        var shown = page.Page.Locator(".quiz-explanation .markdown");
        await Assertions.Expect(shown).ToContainTextAsync("<script>window.__xss = 20</script>");
        await Assertions.Expect(shown).ToContainTextAsync("Hier klicken");
        await AssertNothingRanAsync(page, watch, "h1, .quiz-form, .quiz-feedback");
        Assert.Equal(0, await page.Page.Locator(".quiz-form, .quiz-explanation").Locator("a, img, svg, b, div[onmouseover], [onclick]").CountAsync());
    }

    private static async Task AssertNothingRanAsync(ShellPage page, Watch watch, string shown)
    {
        await page.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal("undefined", await page.Page.EvaluateAsync<string>("() => typeof window.__xss"));
        Assert.Equal(0, await page.Page.Locator(shown).Locator(HostileText.Injected).CountAsync());
        Assert.Empty(watch.Dialogs);
        Assert.Empty(watch.Errors);
    }

    private static Guid Id(int index) => new($"00000000-0000-4000-8000-{index:D12}");

    private static Task FulfillAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        });

    // What the page did besides rendering: dialogs it opened and errors it logged.
    private sealed class Watch
    {
        private readonly List<string> dialogs = [];
        private readonly List<string> errors = [];

        public IReadOnlyList<string> Dialogs
        {
            get
            {
                lock (dialogs)
                {
                    return [.. dialogs];
                }
            }
        }

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (errors)
                {
                    return [.. errors];
                }
            }
        }

        // A load that broke off on the runner's network counts for nothing, only the load that got through does.
        public void Reset()
        {
            lock (dialogs)
            {
                dialogs.Clear();
            }

            lock (errors)
            {
                errors.Clear();
            }
        }

        public void On(IPage page)
        {
            page.Dialog += (_, dialog) =>
            {
                lock (dialogs)
                {
                    dialogs.Add(dialog.Message);
                }

                _ = dialog.DismissAsync();
            };
            page.Console += (_, message) =>
            {
                if (message.Type == "error")
                {
                    Add(message.Text);
                }
            };
            page.PageError += (_, error) => Add(error);
        }

        private void Add(string error)
        {
            lock (errors)
            {
                errors.Add(error);
            }
        }
    }
}