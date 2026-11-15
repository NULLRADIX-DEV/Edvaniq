using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

namespace Edvaniq.Gateway.IntegrationTests;

// #319: the gateway takes a body as large as the upload route of the web host and Content, a larger one never reaches
// a service. What Content says about a file too large gets through to the web host as it is.
public sealed class BodyLimitTests
{
    private const long Limit = 21_037_056;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Also covers every other body, a pasted text of 200.000 characters is well below a megabyte.
    [Fact]
    public async Task BodyAtTheLimit_GetsThrough()
    {
        await using var content = await StubService.StartAsync("content-stub");
        await using var gateway = Gateway(content);
        using var client = RoutingTests.AuthorizedClient(gateway);

        using var response = await client.PostAsync("/content/documents/files", Body(Limit), Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, content.Requests);
    }

    [Fact]
    public async Task BodyOverTheLimit_IsRefusedWithAProblem_AndNotForwarded()
    {
        await using var content = await StubService.StartAsync("content-stub");
        await using var gateway = Gateway(content);
        using var client = RoutingTests.AuthorizedClient(gateway);

        using var response = await client.PostAsync("/content/documents/files", Body(Limit + 1), Cancel);

        await RoutingTests.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "The body is larger than allowed.");
        Assert.Equal(0, content.Requests);
    }

    [Fact]
    public async Task TooLargeFromContent_GetsThroughUnchanged()
    {
        const string Title = "Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.";
        await using var content = await StubService.StartAsync("content-stub");
        content.Fail("POST", "/documents/files", 413, "application/problem+json", $$"""{"status":413,"title":"{{Title}}"}""");
        await using var gateway = Gateway(content);
        using var client = RoutingTests.AuthorizedClient(gateway);

        using var response = await client.PostAsync("/content/documents/files", Body(1024), Cancel);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(Title, problem.GetProperty("title").GetString());
    }

    private static GatewayFactory Gateway(StubService content) =>
        new("http://127.0.0.1:9", services: new Dictionary<string, string> { ["content"] = content.Address });

    private static ByteArrayContent Body(long length)
    {
        var body = new ByteArrayContent(new byte[length]);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=x");
        return body;
    }
}