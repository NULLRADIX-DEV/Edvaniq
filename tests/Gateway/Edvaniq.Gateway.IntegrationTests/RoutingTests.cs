using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

using Yarp.ReverseProxy;

namespace Edvaniq.Gateway.IntegrationTests;

// The gateway forwards /planning/... to Planning and answers everything else itself, with a problem instead of a
// bare status.
public sealed class RoutingTests
{
    [Fact]
    public async Task PlanningRoute_ReachesTheConfiguredTarget_WithoutPrefix()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken);
        Assert.Equal(new Echo("planning-stub", "POST", "/ping", "Bearer token", Cookie: ""), echo);
    }

    // The shell asks the web host, and the web host asks here, whether the backend answers. No service is involved.
    [Fact]
    public async Task Status_AnswersWithoutAnyService()
    {
        await using var gateway = new GatewayFactory("http://127.0.0.1:9");
        using var client = gateway.CreateClient();

        using var response = await client.GetAsync("/status", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // A path with a dot is a case of its own: the default fallback pattern leaves out what looks like a file.
    [Theory]
    [InlineData("/nothing")]
    [InlineData("/style.css")]
    [InlineData("/planningx/ping")]
    public async Task UnknownPath_IsNotFoundProblem(string path)
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = gateway.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "No service answers to this path.");
    }

    [Fact]
    public async Task PlanningDown_IsBadGatewayProblem()
    {
        string address;
        await using (var planning = await StubService.StartAsync("planning-stub"))
        {
            address = planning.Address;
        }

        await using var gateway = new GatewayFactory(address);
        using var client = gateway.CreateClient();

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.BadGateway, "The service 'planning' is not reachable right now.");
    }

    [Fact]
    public async Task PlanningTooSlow_IsGatewayTimeoutProblem()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var gateway = new GatewayFactory(planning.Address, planningTimeout: TimeSpan.FromMilliseconds(200));
        using var client = gateway.CreateClient();

        using var response = await client.GetAsync("/planning/slow", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.GatewayTimeout, "The service 'planning' did not answer in time.");
    }

    // A caller with the standard resilience handler waits 10 seconds per attempt. A cluster that waits longer, like the
    // 100 seconds YARP waits without a setting, never gets its 504 problem to the caller.
    [Fact]
    public void EveryCluster_GivesUpBeforeTheCaller()
    {
        using var gateway = new GatewayFactory("http://127.0.0.1:9");

        var clusters = gateway.Services.GetRequiredService<IProxyStateLookup>().GetClusters().ToList();

        Assert.NotEmpty(clusters);
        Assert.All(clusters, cluster =>
            Assert.True(cluster.Model.Config.HttpRequest?.ActivityTimeout < TimeSpan.FromSeconds(10), cluster.ClusterId));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
    }
}