using System.Text.Json;

using Edvaniq.Testing;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

public sealed class LoginPageTests(WebShell shell)
{
    private const string Address = "kim@example.org";

    // #1013: a password manager or the browser's autofill puts name and password into the fields without a key, and
    // often without any event the page hears of. The first tap on "Anmelden" still signs in.
    [Theory]
    [InlineData(WebShell.PhoneWidth)]
    [InlineData(WebShell.DesktopWidth)]
    public async Task FieldsFilledLikeAPasswordManager_SignInOnTheFirstTap(int width)
    {
        await using var page = await shell.OpenAsync(shell.Address, "/login?returnUrl=%2Fthemen", width);
        await Assertions.Expect(page.Page.GetByLabel("Passwort")).ToBeVisibleAsync();

        await page.Page.EvaluateAsync(
            """
            ([name, password]) => {
                document.getElementById('login-name').value = name;
                document.getElementById('login-password').value = password;
            }
            """,
            new[] { StubService.UserName, StubService.Password });
        await page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Anmelden" }).ClickAsync();

        await Assertions.Expect(page.Page).ToHaveURLAsync(shell.Address + "/themen");
    }

    // #264, #269: each refusal of the web host gets its own hint, on a phone and with the keyboard alone, readable for
    // axe and without a breach of the content security policy.
    [Theory]
    [InlineData(401, "invalid_credentials", null, ".login-hint", "E-Mail, Name oder Passwort stimmen nicht.")]
    [InlineData(429, "locked", 900, ".login-hint", "Warte 15 Minuten")]
    [InlineData(403, "awaiting_approval", null, ".login-approval", "Fast geschafft")]
    [InlineData(502, null, null, ".login-hint", "Die Anmeldung ist gerade nicht möglich.")]
    public async Task ARefusal_ShowsItsHint(int status, string? code, int? retryAfter, string hint, string text)
    {
        await using var page = await shell.OpenAsync(shell.Address, "/login", WebShell.PhoneWidth);
        await RefuseAsync(page.Page, status, code, retryAfter);

        await SignInWithTheKeyboardAsync(page.Page, Address, "irgendein-passwort");

        var shown = page.Page.Locator($"[role=alert] {hint}");
        await Assertions.Expect(shown).ToContainTextAsync(text);
        await Assertions.Expect(page.Page.Locator("#login-password")).ToHaveValueAsync("");
        // Before axe, which brings styles of its own the policy refuses.
        Assert.Empty(page.PolicyViolations);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/login after {status} {code} at {WebShell.PhoneWidth} px");
    }

    // The way to a new link: from the password on to the button with Tab, Enter, and the note says it went out.
    [Fact]
    public async Task Unconfirmed_GetsANewLink_WithTheKeyboard()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/login", WebShell.PhoneWidth);
        await RefuseAsync(page.Page, 403, "unconfirmed", null);
        string? sent = null;
        await page.Page.RouteAsync(shell.Address + "/api/identity/accounts/confirmations/resend", async route =>
        {
            sent = route.Request.PostData;
            await route.FulfillAsync(new RouteFulfillOptions { Status = 202, ContentType = "application/json", Body = """{"mailDelayed":true}""" });
        });

        await SignInWithTheKeyboardAsync(page.Page, Address, "irgendein-passwort");
        var resend = page.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Link neu senden" });
        await Assertions.Expect(resend).ToBeVisibleAsync();
        await TabToAsync(page.Page, ".login-unconfirmed button");
        await Assertions.Expect(resend).ToBeFocusedAsync();
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.Locator("[role=alert] .login-resend-note")).ToContainTextAsync("Die Mail kann sich verspäten.");
        Assert.Equal(Address, JsonDocument.Parse(sent!).RootElement.GetProperty("email").GetString());
        Assert.Empty(page.PolicyViolations);
        await AxeScan.AssertNoViolationsAsync(page.Page, $"/login with a new link at {WebShell.PhoneWidth} px");
    }

    // AK3: signed in, the user lands in the learning area, signed out on the public page.
    [Fact]
    public async Task Login_LeadsToTheLearningArea_AndLogout_ToThePublicPage()
    {
        await using var page = await shell.OpenAsync(shell.Address, "/login", WebShell.PhoneWidth);
        await Material.AnswerPlanAsync(page.Page, Material.Plan());

        await SignInWithTheKeyboardAsync(page.Page, StubService.UserName, StubService.Password);

        await Assertions.Expect(page.Page).ToHaveURLAsync(shell.Address + "/");
        await Assertions.Expect(page.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Ganzen Plan ansehen" })).ToBeVisibleAsync();

        await TabToAsync(page.Page, ".shell-menu-button");
        await page.Page.Keyboard.PressAsync("Enter");
        await TabToAsync(page.Page, "button:text-is('Abmelden')");
        await page.Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.Page.GetByText("Edvaniq ist noch im Aufbau.")).ToBeVisibleAsync();
        Assert.Equal(shell.Address + "/", page.Page.Url);
        Assert.Empty(page.PolicyViolations);
    }

    private Task RefuseAsync(IPage page, int status, string? code, int? retryAfter) =>
        page.RouteAsync(shell.Address + "/session/login", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = status,
            ContentType = code is null ? "text/plain" : "application/problem+json",
            Body = code is null ? "" : JsonSerializer.Serialize(new { status, code, title = Title(code) }),
            Headers = retryAfter is null ? null : new Dictionary<string, string> { ["Retry-After"] = retryAfter.Value.ToString() },
        }));

    private static string? Title(string code) => code switch
    {
        "unconfirmed" => "Bestätige zuerst deine E-Mail-Adresse mit dem Link aus unserer Mail.",
        "awaiting_approval" => "Dein Konto muss noch freigegeben werden.",
        _ => null,
    };

    private static async Task SignInWithTheKeyboardAsync(IPage page, string name, string password)
    {
        await TabToAsync(page, "#login-name");
        await page.Keyboard.TypeAsync(name);
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync(password);
        await page.Keyboard.PressAsync("Enter");
    }

    // Presses Tab until the element has the focus, at most 30 times.
    private static async Task TabToAsync(IPage page, string selector)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await page.Locator(selector).EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.Fail($"Tab never reached {selector}.");
    }
}