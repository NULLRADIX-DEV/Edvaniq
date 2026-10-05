using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// POST /ping writes into the service's own database and answers with what it read back from there.
public sealed class PingTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task Ping_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.PostAsync("/ping", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_AnswersWithThePingAsStored()
    {
        var before = DateTime.UtcNow;
        var ping = await PingAsync();
        var after = DateTime.UtcNow;

        // MySQL keeps microseconds, the clock of .NET counts in 100 ns. The stored time may be a bit earlier.
        Assert.InRange(ping.PingedAt, before.AddMilliseconds(-1), after);
        Assert.Equal((await StoredAsync(ping.Id)).PingedAt, ping.PingedAt);
    }

    [Fact]
    public async Task Ping_Twice_StoresTheSecondPing()
    {
        var first = await PingAsync();
        var second = await PingAsync();

        Assert.True(second.PingedAt > first.PingedAt, "The second ping did not write a new time.");
        Assert.Equal((await StoredAsync(second.Id)).PingedAt, second.PingedAt);
    }

    // Without the "Z" a browser reads the time as local time and shows it two hours off in summer.
    [Fact]
    public async Task Ping_AnswersWithUtc()
    {
        using var response = await PostPingAsync();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.EndsWith("Z", json.GetProperty("pingedAt").GetString());
    }

    private async Task<Ping> PingAsync()
    {
        using var response = await PostPingAsync();
        return (await response.Content.ReadFromJsonAsync<Ping>(TestContext.Current.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> PostPingAsync()
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create("pinger"));

        var response = await client.PostAsync("/ping", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private async Task<PingRecord> StoredAsync(int id)
    {
        using var scope = service.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PlanningDbContext>().Pings.AsNoTracking()
            .SingleAsync(ping => ping.Id == id, TestContext.Current.CancellationToken);
    }

    private sealed record Ping(int Id, DateTime PingedAt);
}
