using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #251: an account logs in with its address in the same field as a team account's name. What went wrong comes as a
// problem with a code the client tells apart. Why an account may not log in yet is said only after the right password,
// Identity gives no reason for a wrong one.
public sealed class LoginRefusalTests
{
    [Theory]
    [InlineData("unconfirmed")]
    [InlineData("awaiting_approval")]
    public async Task AccountNotActiveYet_Is403_WithItsCode_AndNoSession(string reason)
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.LoginRefusal = reason;
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await LoginAsync(browser, StubService.UserName, StubService.Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(reason, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task WrongPassword_Is401_InvalidCredentials()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await LoginAsync(browser, "lena@example.org", "ein falsches Passwort");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TooManyAttempts_Is429_WithItsCode()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        gateway.LoginStatus = 429;
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await LoginAsync(browser, "lena@example.org", "ein langes Passwort");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("too_many_attempts", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString());
    }

    // The address goes to Identity as the login, as typed.
    [Fact]
    public async Task Address_GoesToIdentity_AsTheLogin()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser(handleCookies: false);

        using var response = await LoginAsync(browser, "Lena@Example.org", "ein langes Passwort");

        Assert.Equal("Lena@Example.org", gateway.LastLoginRequest["username"]);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient browser, string name, string password) =>
        browser.PostAsJsonAsync("/session/login", new { name, password }, TestContext.Current.CancellationToken);
}