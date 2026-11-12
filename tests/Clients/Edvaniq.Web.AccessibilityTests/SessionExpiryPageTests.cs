using System.Net;

using Edvaniq.Testing;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #197: the login ends on the server while someone works. The next call gets 401 from the real web host, the shell says
// so in German with the way to the login, and after the login the page is back with what was not sent yet.
public sealed class SessionExpiryPageTests(WebShell shell)
{
    private static string QuizPath => $"/themen/{Material.TopicId}/ueben";

    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task PastedText_SurvivesTheLogin_AndTheWayBack(int width)
    {
        var ended = false;
        await using var page = await shell.OpenSignedInAsync("/themen", width, prepare: async browser =>
        {
            await Material.AnswerAsync(browser, documents: [new[] { Material.Document("Ready") }], topics: [Material.Topics]);

            // While the login is ended, the paste goes to the real web host.
            await browser.RouteAsync("**/api/content/documents", route =>
                ended && route.Request.Method == "POST" ? route.ContinueAsync() : route.FallbackAsync());
        });
        await Material.ConfirmAsync(page.Page);
        await page.Page.GetByLabel("Text", new PageGetByLabelOptions { Exact = true }).FillAsync("Die Lichtreaktion läuft in den Thylakoiden ab.");

        ended = await EndLoginAsync(page);
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Themen erkennen" }).ClickAsync();

        var notice = page.Page.Locator(".session-ended");
        await Assertions.Expect(notice).ToContainTextAsync("Deine Anmeldung ist abgelaufen.");
        await Assertions.Expect(page.Page.Locator(".material-status")).ToContainTextAsync("Dein Text ist noch nicht angekommen.");
        Assert.DoesNotMatch("""\d{3}|Exception|http|/api/""", await notice.InnerTextAsync());
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/themen with a login that ran out at {width} px");

        await SignInAgainAsync(page, "/themen");
        ended = false;

        await Assertions.Expect(page.Page).ToHaveURLAsync(shell.Address + "/themen");
        await Assertions.Expect(page.Page.GetByLabel("Text", new PageGetByLabelOptions { Exact = true }))
            .ToHaveValueAsync("Die Lichtreaktion läuft in den Thylakoiden ab.");
        await Assertions.Expect(page.Page.Locator(".paste-form [role=alert]")).ToContainTextAsync("Dein Text von vorhin ist noch da.");
        await Assertions.Expect(notice).ToBeEmptyAsync();
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/themen with the text back at {width} px");
    }

    // The answer that met the ended login never reached Assessment. Back from the login it goes to the same attempt,
    // which Assessment grades once.
    [Fact]
    public async Task OpenAnswer_GoesToTheSameAttempt_AfterTheLogin()
    {
        var ended = false;
        var answers = new List<(string Url, bool Ended)>();
        await using var page = await shell.OpenSignedInAsync(QuizPath, WebShell.PhoneWidth, prepare: async browser =>
        {
            await Material.AnswerQuizAsync(browser);
            await browser.RouteAsync("**/api/assessment/attempts/*/answer", route =>
            {
                lock (answers)
                {
                    answers.Add((route.Request.Url, ended));
                }

                return ended ? route.ContinueAsync() : route.FallbackAsync();
            });
        });
        await page.Page.GetByLabel("In den Thylakoiden").CheckAsync();
        var check = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Antwort prüfen" });

        ended = await EndLoginAsync(page);
        await check.ClickAsync();

        await Assertions.Expect(page.Page.Locator(".session-ended")).ToContainTextAsync("Deine Anmeldung ist abgelaufen.");
        await Assertions.Expect(page.Page.Locator("form [role=alert]")).ToContainTextAsync("Deine Antwort ist noch nicht angekommen.");
        await AxeScan.AssertNoViolationsAsync(page.Page, $"{QuizPath} with a login that ran out at {WebShell.PhoneWidth} px");

        await SignInAgainAsync(page, QuizPath);
        ended = false;

        await Assertions.Expect(page.Page).ToHaveURLAsync(shell.Address + QuizPath);
        await Assertions.Expect(page.Page.GetByLabel("In den Thylakoiden")).ToBeCheckedAsync();
        await check.ClickAsync();

        await Assertions.Expect(page.Page.Locator("#feedback-heading")).ToContainTextAsync("Richtig! Gut gemacht.");
        Assert.Equal(2, answers.Count);
        Assert.Equal(answers[0].Url, answers[1].Url);
        Assert.Equal([true, false], answers.Select(answer => answer.Ended));
    }

    // Ends the login on the server, as a logout in another tab does. The page's cookie is refused from then on.
    private static async Task<bool> EndLoginAsync(ShellPage page)
    {
        var url = page.Page.Url;
        var logout = await page.Page.Context.APIRequest.DeleteAsync(new Uri(new Uri(url), "/session").ToString());
        Assert.Equal((int)HttpStatusCode.NoContent, logout.Status);
        return true;
    }

    private async Task SignInAgainAsync(ShellPage page, string back)
    {
        await page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Neu anmelden" }).ClickAsync();
        await Assertions.Expect(page.Page).ToHaveURLAsync(shell.Address + "/login?returnUrl=" + Uri.EscapeDataString(back));
        await Assertions.Expect(page.Page.Locator(".login-ended")).ToContainTextAsync("Nach dem Anmelden geht es dort weiter");

        await page.Page.GetByLabel("Name").FillAsync(StubService.UserName);
        await page.Page.GetByLabel("Passwort").FillAsync(StubService.Password);
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Anmelden" }).ClickAsync();
    }
}