using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Api;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.IntegrationTests;

// #305: the operator's emergency stop. Paused, no AI call of Content reaches the provider, every document that needs
// the AI fails as "KI derzeit pausiert" and can be retried once it runs again, and everything else keeps working. One
// class with its own database: the switch is one row for the whole service, the tests run one after the other.
public sealed class AiSwitchTests(ServiceFactory service) : IClassFixture<ServiceFactory>, IAsyncLifetime
{
    private readonly IHost ai = SimulatedAi();

    private int asked;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    // Every test leaves the AI running.
    public async ValueTask DisposeAsync()
    {
        await SetAsync(paused: false);
        ai.Dispose();
    }

    [Fact]
    public async Task Command_PausesResumesAndTellsTheState_LoggingOnlyRealChanges()
    {
        Assert.Equal((AiCommand.Ok, "AI running"), await RunAsync("ai", "status"));
        Assert.Equal((AiCommand.Ok, "AI paused"), await RunAsync("ai", "pause"));
        Assert.Equal((AiCommand.Ok, "AI paused"), await RunAsync("ai", "pause"));
        Assert.True(await PausedAsync());
        Assert.Equal((AiCommand.Ok, "AI running"), await RunAsync("ai", "resume"));
        Assert.False(await PausedAsync());

        Assert.Single(service.Logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.StartsWith("The operator paused the AI", StringComparison.Ordinal));
        Assert.Single(service.Logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message == "The operator resumed the AI");
    }

    [Theory]
    [InlineData]
    [InlineData("ai")]
    [InlineData("ai", "stop")]
    [InlineData("ai", "pause", "now")]
    public async Task Command_WithWrongArguments_ExitsWith2(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(AiCommand.Invalid, await AiCommand.RunAsync(service.Services, args, output, error));
        Assert.Contains("Usage: ai pause|resume|status", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    // A new and a postponed document both fail without the provider asked, the list still answers, and after resume a
    // retry makes the new one ready to go on.
    [Fact]
    public async Task Paused_FailsNewAndPostponedDocuments_WithoutAskingTheAi_AndRetryWorksAfterResume()
    {
        using var client = ClientFor($"alice-{Guid.NewGuid():N}");
        var fresh = await PasteAsync(client);
        var postponed = await PasteAsync(client);
        await SplitAsync(fresh);
        await SplitAsync(postponed);
        await PostponeAsync(postponed);

        await SetAsync(paused: true);
        Assert.Null(Stored(postponed).NextAttemptAt);
        await Analyzer().AnalyzeAsync(fresh, Cancel);
        await Analyzer().AnalyzeAsync(postponed, Cancel);

        Assert.Equal(0, asked);
        foreach (var id in new[] { fresh, postponed })
        {
            var failed = await client.GetFromJsonAsync<DocumentDto>($"/documents/{id}", Cancel);
            Assert.Equal(("Failed", Document.AiPaused), (failed!.Status, failed.FailureReason));
        }

        using var list = await client.GetAsync("/documents", Cancel);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        await SetAsync(paused: false);
        using var retry = await client.PostAsync($"/documents/{fresh}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        await SplitAsync(fresh);
        await Analyzer().AnalyzeAsync(fresh, Cancel);

        Assert.Equal(1, asked);
        Assert.NotNull(Stored(fresh).AnalyzedAt);
    }

    // The worker puts the switch in front of its AI, so the operator's ai-check stops too.
    [Fact]
    public async Task Paused_StopsEveryCallThroughTheWorkersAi()
    {
        await SetAsync(paused: true);

        await Assert.ThrowsAsync<AiPausedException>(() => Chat().GetResponseAsync("Say ok.", cancellationToken: Cancel));
        Assert.Equal(0, asked);
    }

    private async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await AiCommand.RunAsync(service.Services, args, output, error);
        Assert.Empty(error.ToString());
        return (exitCode, output.ToString().Trim().Split(',')[0]);
    }

    private async Task SetAsync(bool paused)
    {
        using var scope = service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAiSwitch>().SetAsync(paused, CancellationToken.None);
    }

    private async Task<bool> PausedAsync()
    {
        using var scope = service.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAiSwitch>().IsPausedAsync(Cancel);
    }

    // As after an outage of the provider: the document waits a quarter of an hour.
    private async Task PostponeAsync(Guid id)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = await db.Documents.IgnoreQueryFilters().SingleAsync(item => item.Id == id, Cancel);
        document.Postpone(DateTime.UtcNow, [TimeSpan.FromMinutes(15)], null);
        await db.SaveChangesAsync(Cancel);
    }

    private Document Stored(Guid id)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters().AsNoTracking().Single(item => item.Id == id);
    }

    private Task SplitAsync(Guid id) => ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);

    // The worker's AI: the simulated provider behind the switch, counting what reaches the provider.
    private IChatClient Chat() => new AiGate(
        new CountingChatClient(ai.Services.GetRequiredService<IChatClient>(), () => asked++),
        service.Services.GetRequiredService<IServiceScopeFactory>(),
        service.Services.GetRequiredService<ILogger<AiGate>>());

    private DocumentAnalyzer Analyzer()
    {
        var analyzer = ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, Chat());
        analyzer.OutageDelays = [];
        return analyzer;
    }

    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
        builder.AddContentAi();
        return builder.Build();
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Guid> PasteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Cancel))!.Id;
    }

    private sealed record DocumentDto(Guid Id, string Status, string? FailureReason);

    private sealed class CountingChatClient(IChatClient inner, Action count) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            count();
            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }
}