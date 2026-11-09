using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

namespace Edvaniq.Gateway.IntegrationTests;

// A service that fails with an error page or an empty body tells nothing about itself through the gateway, not even in
// its headers. A problem of the service gets through unchanged, so does everything below 500.
public sealed class ServiceErrorTests
{
    [Fact]
    public async Task ErrorPage_IsReplacedByAProblem_WithoutTheHeadersOfTheService()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        planning.Fail("POST", "/ping", 500, "text/html", "<html><body>NullReferenceException at Planning.Ping()</body></html>");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = RoutingTests.AuthorizedClient(gateway);

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        var problem = await RoutingTests.AssertProblemAsync(response, HttpStatusCode.InternalServerError, "The service 'planning' failed.");
        Assert.DoesNotContain("NullReferenceException", problem.GetRawText());
        Assert.False(response.Headers.Contains("X-Powered-By"));
        Assert.Empty(response.Headers.Server);
    }

    [Fact]
    public async Task EmptyError_IsReplacedByAProblem()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        planning.Fail("POST", "/ping", 503);
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = RoutingTests.AuthorizedClient(gateway);

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        await RoutingTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "The service 'planning' failed.");
        Assert.False(response.Headers.Contains("X-Powered-By"));
    }

    [Fact]
    public async Task ProblemOfTheService_GetsThroughUnchanged()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        planning.Fail("POST", "/ping", 500, "application/problem+json", """{"status":500,"traceId":"from-the-service"}""");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = RoutingTests.AuthorizedClient(gateway);

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("from-the-service", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task ClientError_GetsThroughUnchanged()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        planning.Fail("POST", "/ping", 409, "text/plain", "conflict");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = RoutingTests.AuthorizedClient(gateway);

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}