using System.Text.Json;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #294: the topic directory on a phone. Topics open with the keyboard and show their key terms and sources, hidden ones
// sit apart, nothing scrolls sideways, and each state passes axe-core.
public sealed class TopicDirectoryPageTests(WebShell shell)
{
    private const string Path = "/themen/verzeichnis";
    private const float SmallestTarget = 44;

    private static readonly Guid Section = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    [Fact]
    public async Task TopicsOpen_ByKeyboard_OnAPhone_WithTermsAndSources()
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: AnswerAsync);
        var topic = Toggle(page.Page, "Photosynthese");
        await Assertions.Expect(topic).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.Page.Locator(".directory-ai-hint")).ToContainTextAsync("Von der KI vorgeschlagen");
        await AssertLargeEnoughAsync(topic);
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} closed at {WebShell.PhoneWidth} px");

        await topic.FocusAsync();
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(topic).ToHaveAttributeAsync("aria-expanded", "true");
        var panel = page.Page.Locator($"#{await topic.GetAttributeAsync("aria-controls")}");
        await Assertions.Expect(panel.Locator("dt").First).ToHaveTextAsync("Chlorophyll");
        await Assertions.Expect(panel.Locator(".topic-sources").First).ToHaveTextAsync("Skript Biologie: S. 3–4");

        var subtopic = Toggle(page.Page, "Lichtreaktion");
        await subtopic.FocusAsync();
        await page.Page.Keyboard.PressAsync("Space");
        await Assertions.Expect(subtopic).ToHaveAttributeAsync("aria-expanded", "true");

        var hidden = page.Page.Locator(".directory-hidden > h2 button");
        await hidden.ClickAsync();
        await Assertions.Expect(page.Page.Locator("#hidden-topics")).ToContainTextAsync("Zellatmung");

        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} open at {WebShell.PhoneWidth} px");
    }

    // #295: rename, hide with undo and add on a phone with the keyboard only. The focus follows each step, and each state
    // passes axe-core.
    [Fact]
    public async Task TopicsChange_ByKeyboard_OnAPhone()
    {
        var changes = new List<string>();
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: async browser =>
        {
            await AnswerAsync(browser);
            await AnswerChangesAsync(browser, changes);
        });
        var keys = page.Page.Keyboard;
        var topic = Toggle(page.Page, "Photosynthese");
        await topic.FocusAsync();
        await keys.PressAsync("Enter");
        var panel = page.Page.Locator($"#{await topic.GetAttributeAsync("aria-controls")}");
        await Assertions.Expect(panel.Locator("dt").First).ToHaveTextAsync("Chlorophyll");

        var rename = panel.Locator(":scope > .topic-tools button", new LocatorLocatorOptions { HasText = "Umbenennen" });
        await AssertLargeEnoughAsync(rename);
        await rename.FocusAsync();
        await keys.PressAsync("Enter");
        var field = page.Page.Locator($"#rename-{Material.TopicId:N}");
        await Assertions.Expect(field).ToBeFocusedAsync();
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} renaming at {WebShell.PhoneWidth} px");
        await keys.PressAsync("Control+A");
        await keys.TypeAsync("Fotosynthese");
        await keys.PressAsync("Enter");

        var status = page.Page.Locator(".directory-edit-status");
        await Assertions.Expect(status).ToContainTextAsync("Das Thema heißt jetzt „Fotosynthese“.");
        await Assertions.Expect(rename).ToBeFocusedAsync();

        await keys.PressAsync("Tab");
        await keys.PressAsync("Tab");
        await Assertions.Expect(panel.Locator(":scope > .topic-tools .topic-hide")).ToBeFocusedAsync();
        await keys.PressAsync("Enter");

        await Assertions.Expect(status).ToContainTextAsync("„Photosynthese“ ist samt 2 Unterthemen ausgeblendet.");
        var undo = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Rückgängig" });
        await Assertions.Expect(undo).ToBeFocusedAsync();
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} hidden at {WebShell.PhoneWidth} px");
        await keys.PressAsync("Enter");
        await Assertions.Expect(status).ToContainTextAsync("„Photosynthese“ ist wieder eingeblendet.");

        await page.Page.Locator("#topic-add-name").FocusAsync();
        await keys.TypeAsync("Zellteilung");
        await keys.PressAsync("Enter");
        await Assertions.Expect(status).ToContainTextAsync("„Zellteilung“ ist hinzugefügt.");

        Assert.Equal(
            [
                $"PATCH /api/knowledge/topics/{Material.TopicId}",
                $"PUT /api/knowledge/topics/{Material.TopicId}/hidden",
                $"DELETE /api/knowledge/topics/{Material.TopicId}/hidden",
                "POST /api/knowledge/topics",
            ],
            changes);
        await AssertNoSidewaysScrollAsync(page.Page);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} added at {WebShell.PhoneWidth} px");
    }

    [Theory]
    [InlineData("Analyzing", "Die Analyse läuft noch.")]
    [InlineData(null, "Du hast noch kein Material.")]
    public async Task EmptyStates_OnAPhone_HaveNoViolations(string? status, string says)
    {
        await using var page = await shell.OpenSignedInAsync(Path, WebShell.PhoneWidth, prepare: browser =>
            Material.AnswerAsync(
                browser,
                documents: [status is null ? Array.Empty<object>() : new[] { Material.Document(status) }],
                topics: [Array.Empty<object>()]));

        await Assertions.Expect(page.Page.Locator(".directory-status")).ToContainTextAsync(says);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{Path} empty ({status ?? "no material"}) at {WebShell.PhoneWidth} px");
    }

    private static ILocator Toggle(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name, Exact = true });

    // The visible topics of Material plus a hidden one, a key term, and one section on pages 3 to 4 of the document.
    internal static async Task AnswerAsync(IPage page)
    {
        object[] topics =
        [
            .. Material.Topics,
            new { id = Guid.NewGuid(), learningGoalId = Guid.Empty, name = "Zellatmung", documentId = (Guid?)null, position = 3, hidden = true, subtopics = Array.Empty<object>() },
        ];
        await Material.AnswerAsync(page, documents: [new[] { Material.Document("Ready") }], topics: [topics]);
        await page.RouteAsync("**/api/knowledge/topics?includeHidden=true", route => FulfillAsync(route, topics));
        await page.RouteAsync("**/api/knowledge/topics/*/terms", route => FulfillAsync(route, new[]
        {
            new { term = "Chlorophyll", definition = "Grüner Farbstoff, der Licht aufnimmt.", documentId = Material.DocumentId, sectionId = Section },
        }));
        await page.RouteAsync("**/api/knowledge/topics/*/sections", route => FulfillAsync(route, new[]
        {
            new { documentId = Material.DocumentId, sectionId = Section },
        }));
        await page.RouteAsync("**/api/content/documents/*/sections", route => FulfillAsync(route, new[]
        {
            new { sectionId = Section, position = 0, pageFrom = 3, pageTo = 4 },
        }));
    }

    // Knowledge takes every change and answers with the topic. The list stays as it was, only the page's messages count.
    private static async Task AnswerChangesAsync(IPage page, List<string> changes)
    {
        async Task ChangeAsync(IRoute route)
        {
            var request = route.Request;
            if (request.Method == "GET")
            {
                await route.FallbackAsync();
                return;
            }

            lock (changes)
            {
                changes.Add($"{request.Method} {new Uri(request.Url).AbsolutePath}");
            }

            var name = "Photosynthese";
            if (request.PostData is { Length: > 0 } body)
            {
                using var sent = JsonDocument.Parse(body);
                name = sent.RootElement.GetProperty("name").GetString()!;
            }

            await FulfillAsync(
                route,
                new { id = request.Method == "POST" ? Guid.NewGuid() : Material.TopicId, learningGoalId = Guid.Empty, name, position = 0, hidden = request.Method == "PUT" },
                request.Method == "POST" ? 201 : 200);
        }

        await page.RouteAsync("**/api/knowledge/topics", ChangeAsync);
        await page.RouteAsync($"**/api/knowledge/topics/{Material.TopicId}", ChangeAsync);
        await page.RouteAsync($"**/api/knowledge/topics/{Material.TopicId}/hidden", ChangeAsync);
    }

    private static Task FulfillAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        });

    private static async Task AssertLargeEnoughAsync(ILocator target)
    {
        var box = await target.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Height >= SmallestTarget, $"The target is {box.Height} px high.");
        Assert.True(box.Width >= SmallestTarget, $"The target is {box.Width} px wide.");
    }

    private static async Task AssertNoSidewaysScrollAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 0, $"The page scrolls {overflow} px sideways.");
    }
}