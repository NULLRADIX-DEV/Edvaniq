using Edvaniq.Testing;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// #1013: a password manager or the browser's autofill puts name and password into the fields without a key, and often
// without any event the page hears of. The first tap on "Anmelden" still signs in.
public sealed class LoginPageTests(WebShell shell)
{
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
}