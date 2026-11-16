using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #352: the browser runs only the scripts the web host sent as files, and the import map that carries the nonce of its
// answer. Whatever makes it into a page some other way stays dead, and the shell's own pages break no rule of the policy.
public sealed class ContentSecurityPolicyTests(WebShell shell)
{
    private const string Injected =
        """
        <script>window.edvaniqInjected = 'inline';</script>
        <script nonce="AAAAAAAAAAAAAAAAAAAAAA==">window.edvaniqInjected = 'guessed nonce';</script>
        <img src="/gibt-es-nicht.png" alt="" onerror="window.edvaniqInjected = 'handler'">
        """;

    public static TheoryData<string> CorePages { get; } =
    [
        "/",
        "/themen",
        "/themen/verzeichnis",
        $"/themen/{Material.TopicId}/ueben",
        $"/themen/{Material.TopicId}/quiz/{Material.BlockId}",
        $"/themen/{Material.TopicId}/zusammenfassung",
        "/lernziele",
        "/lernziele/neu",
        "/plan",
        "/einstellungen",
        "/systemstatus",
    ];

    [Fact]
    public async Task AnInjectedScript_IsBlocked_AndReported()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/login", WebShell.DesktopWidth);
        await page.Page.RouteAsync(shell.Address + "/", async route =>
        {
            var response = await route.FetchAsync();
            var html = await response.TextAsync();
            await route.FulfillAsync(new RouteFulfillOptions { Response = response, Body = html.Replace("</body>", Injected + "</body>", StringComparison.Ordinal) });
        });

        await page.LoadAsync(shell.Address + "/");

        Assert.Null(await page.Page.EvaluateAsync<string?>("() => window.edvaniqInjected ?? null"));
        Assert.Contains(page.PolicyViolations, violation => violation.StartsWith("event: script-src-elem blocked inline", StringComparison.Ordinal));
        Assert.Contains(page.PolicyViolations, violation => violation.StartsWith("event: script-src-attr blocked inline", StringComparison.Ordinal));
        Assert.Equal(2, page.PolicyViolations.Count(violation => violation.StartsWith("event: script-src-elem", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheLoginPage_BreaksNoRule()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/login", WebShell.DesktopWidth);

        Assert.Empty(page.PolicyViolations);
    }

    [Theory]
    [MemberData(nameof(CorePages))]
    public async Task ACorePage_BreaksNoRule(string path)
    {
        await using var page = await shell.OpenSignedInAsync(path, WebShell.DesktopWidth, prepare: browser => AnswerAsync(browser, path));

        Assert.Empty(page.PolicyViolations);
    }

    // Each page gets what it shows, so it renders its content and not only an empty state.
    private static Task AnswerAsync(IPage browser, string path) => path switch
    {
        "/" => Material.AnswerPlanAsync(browser, Material.Plan()),
        "/themen" => Material.AnswerAsync(browser, documents: [new[] { Material.Document("Ready") }], topics: [Material.Topics]),
        "/themen/verzeichnis" => TopicDirectoryPageTests.AnswerAsync(browser),
        "/lernziele" => Material.AnswerGoalListAsync(browser, Material.Goals),
        "/lernziele/neu" => Material.AnswerGoalsAsync(browser, []),
        "/plan" => Material.AnswerWeekAsync(browser, Material.Week()),
        "/einstellungen" => SettingsPageTests.AnswerAsync(browser, []),
        _ when path.EndsWith("/ueben", StringComparison.Ordinal) => Material.AnswerQuizAsync(browser),
        _ when path.Contains("/quiz/", StringComparison.Ordinal) => Material.AnswerBlockQuizAsync(browser, []),
        _ when path.EndsWith("/zusammenfassung", StringComparison.Ordinal) => SummaryPageTests.AnswerSummariesAsync(browser),
        _ => Task.CompletedTask,
    };
}