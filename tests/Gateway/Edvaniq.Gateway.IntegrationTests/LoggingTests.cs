using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;

namespace Edvaniq.Gateway.IntegrationTests;

// The gateway passes on what lets the caller in, the token to a service and the client secret to Identity. Its log
// on the server shows neither.
public sealed class LoggingTests
{
    [Fact]
    public async Task Forwarding_LogsNeitherTokenNorClientSecret()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var planning = await StubService.StartAsync("planning-stub");
        await using var identity = await StubService.StartAsync("identity-stub");
        await using var gateway = new GatewayFactory(
            planning.Address, identityAddress: identity.Address, configure: builder => builder.UseEnvironment("Production"));
        using var client = gateway.CreateClient();
        using var ping = new HttpRequestMessage(HttpMethod.Post, "/planning/ping");
        ping.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "web",
            ["client_secret"] = secret,
        });

        using var pinged = await client.SendAsync(ping, TestContext.Current.CancellationToken);
        using var tokenResponse = await client.PostAsync("/identity/connect/token", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, pinged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        // The log saw both requests, so it had the chance to show what they carried.
        Assert.NotEmpty(gateway.Logs.Containing(planning.Address));
        Assert.NotEmpty(gateway.Logs.Containing(identity.Address));
        Assert.Empty(gateway.Logs.Containing(token, secret));
    }
}