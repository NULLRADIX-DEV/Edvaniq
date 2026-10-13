using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

namespace Edvaniq.Gateway.IntegrationTests;

// A request keeps the trace of its caller, the web host, on its way to the service. The gateway's own problems name
// that trace, so an error report leads to the log lines of every process the request went through.
public sealed class TraceTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task TheServiceGetsTheCallersTrace()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = Client(gateway);

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TraceId, planning.LastTraceParent.Split('-')[1]);
    }

    [Fact]
    public async Task PlanningDown_TheProblemNamesTheCallersTrace()
    {
        string address;
        await using (var planning = await StubService.StartAsync("planning-stub"))
        {
            address = planning.Address;
        }

        await using var gateway = new GatewayFactory(address);
        using var client = Client(gateway);

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(TraceId, await TraceIdOfAsync(response));
    }

    [Fact]
    public async Task UnknownPath_TheProblemNamesTheCallersTrace()
    {
        await using var gateway = new GatewayFactory("http://127.0.0.1:9");
        using var client = Client(gateway);

        using var response = await client.GetAsync("/unknown", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(TraceId, await TraceIdOfAsync(response));
    }

    // The web host as caller, in the middle of its own trace.
    private static HttpClient Client(GatewayFactory gateway)
    {
        var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");
        return client;
    }

    private static async Task<string?> TraceIdOfAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return problem.GetProperty("traceId").GetString();
    }
}