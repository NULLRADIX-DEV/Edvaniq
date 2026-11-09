using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

using Yarp.ReverseProxy;

namespace Edvaniq.Gateway.IntegrationTests;

// The gateway lets nothing through to a service without a valid token. Only the token endpoint of Identity, the status
// check, health and the 404 for unknown paths answer without one.
public sealed class TokenTests
{
    private const string TokenRoute = "identity-token";

    // Goes through the routes of appsettings.json, so a new route is covered without a change here.
    [Fact]
    public async Task EveryRouteButTheTokenEndpoint_WithoutToken_IsUnauthorizedProblem()
    {
        await using var stub = await StubService.StartAsync("stub");
        await using var gateway = new GatewayFactory(stub.Address, identityAddress: stub.Address);
        var routes = gateway.Services.GetRequiredService<IProxyStateLookup>().GetRoutes()
            .Select(route => route.Config)
            .Where(route => route.RouteId != TokenRoute)
            .ToList();
        using var client = gateway.CreateClient();

        Assert.True(routes.Count >= 5, "Expected a route per active service.");
        foreach (var route in routes)
        {
            var path = route.Match.Path!.Replace("{**rest}", "anything", StringComparison.Ordinal);
            var method = route.Match.Methods?.FirstOrDefault() ?? "GET";

            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

            await RoutingTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "This path needs a valid token.");
            Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        }

        Assert.Equal(0, stub.Requests);
    }

    [Fact]
    public void OnlyTheTokenEndpoint_IsAnonymous()
    {
        using var gateway = new GatewayFactory("http://127.0.0.1:9");

        var open = gateway.Services.GetRequiredService<IProxyStateLookup>().GetRoutes()
            .Where(route => route.Config.AuthorizationPolicy is not null)
            .Select(route => $"{route.Config.RouteId}={route.Config.AuthorizationPolicy}");

        Assert.Equal([$"{TokenRoute}=anonymous"], open);
    }

    public static TheoryData<string> BadTokens() =>
    [
        "not-a-token",
        TestTokens.Create("alice", expires: DateTime.UtcNow.AddMinutes(-10)),
        TestTokens.Create("alice", signingKey: TestTokens.NewKey()),
        TestTokens.Create("alice", issuer: "someone-else"),
        TestTokens.Create("alice", audience: "another-app"),
    ];

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task BadToken_IsUnauthorizedProblem_AndNothingIsForwarded(string token)
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        await RoutingTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "This path needs a valid token.");
        Assert.Equal(0, planning.Requests);
    }

    // Every token from Identity names a subject, the account or the client.
    [Fact]
    public async Task TokenWithoutSubject_IsForbiddenProblem()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var gateway = new GatewayFactory(planning.Address);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId: null));

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, planning.Requests);
    }

    // Identity is down, so the gateway can't fetch the keys. The token may be fine, the caller should try again.
    [Fact]
    public async Task KeysOfIdentityUnavailable_IsServiceUnavailableProblem_WithTraceId()
    {
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var gateway = new GatewayFactory(planning.Address, testKeys: false, configure: builder =>
        {
            builder.UseSetting("Authentication:Schemes:Bearer:Authority", "http://127.0.0.1:9");
            builder.UseSetting("Authentication:Schemes:Bearer:RequireHttpsMetadata", "false");
        });
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create("alice"));

        using var response = await client.PostAsync("/planning/ping", content: null, TestContext.Current.CancellationToken);

        var problem = await RoutingTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Tokens can't be checked right now.");
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
        Assert.NotEmpty(gateway.Logs.Containing("The keys of the token issuer can't be fetched"));
        Assert.Equal(0, planning.Requests);
    }
}