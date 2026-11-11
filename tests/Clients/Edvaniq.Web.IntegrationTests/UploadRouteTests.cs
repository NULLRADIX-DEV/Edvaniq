using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

namespace Edvaniq.Web.IntegrationTests;

// #275: the upload route takes a file of up to 20 MB in its multipart frame. A body that says it's larger is turned
// away with a problem before a byte of it is read or forwarded. Other routes keep the default limit.
public sealed class UploadRouteTests
{
    private const long Limit = 21_037_056;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task File_ReachesContent_WithTheGoalInTheQuery()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var goal = Guid.NewGuid();

        using var response = await browser.PostAsync($"/api/content/documents/files?learningGoalId={goal}", Form(1024), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(Cancel))!;
        Assert.Equal(("/content/documents/files", $"?learningGoalId={goal}"), (echo.Path, echo.Query));
    }

    // A file of 20 MB in its frame still fits.
    [Fact]
    public async Task FileOf20MB_GetsThrough()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.PostAsync("/api/content/documents/files", Form(20 * 1024 * 1024), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BodyOverTheLimit_IsRefusedWithAProblem_AndNotForwarded()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);
        var before = gateway.Requests;

        using var response = await browser.PostAsync("/api/content/documents/files", Body(Limit + 1), Cancel);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.", problem.GetProperty("title").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal(before, gateway.Requests);
    }

    // The limit belongs to the upload route alone, a long pasted text is no file.
    [Fact]
    public async Task OtherRoutes_KeepTheirOwnLimit()
    {
        await using var gateway = await StubService.StartAsync("gateway-stub");
        await using var web = new WebFactory(gateway.Address);
        using var browser = web.CreateBrowser();
        await LoginTests.LoginCookieAsync(browser);

        using var response = await browser.PostAsync("/api/content/documents", Body(Limit + 1, "application/json"), Cancel);

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static MultipartFormDataContent Form(int fileLength)
    {
        var file = new ByteArrayContent(new byte[fileLength]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { file, "file", "Skript.pdf" } };
    }

    private static ByteArrayContent Body(long length, string type = "multipart/form-data; boundary=x")
    {
        var body = new ByteArrayContent(new byte[length]);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        return body;
    }
}