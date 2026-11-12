using System.Globalization;
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

// #303: the operator's monthly budget for the AI. Used up, new jobs are turned away in the Api and the worker asks the
// AI nothing, at 80 % and at 100 % the operator gets one log line each per month. Every month starts at 0. The budget is
// one row for the whole service, so the tests of this class share their database and run one after the other.
public sealed class AiBudgetTests(ServiceFactory service) : IClassFixture<ServiceFactory>, IAsyncLifetime
{
    private int asked;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    // Every test leaves the AI without a budget.
    public async ValueTask DisposeAsync()
    {
        await SetBudgetAsync(null);
    }

    [Fact]
    public async Task Command_SetsShowsAndTakesAwayTheBudget_AndLogsNoAmount()
    {
        var (setExit, set) = await RunAsync("ai", "budget", "12.50");
        var (_, shown) = await RunAsync("ai", "status");
        var (noneExit, none) = await RunAsync("ai", "budget", "none");

        Assert.Equal((AiCommand.Ok, AiCommand.Ok), (setExit, noneExit));
        Assert.Contains(" of 12.50 this month (", set, StringComparison.Ordinal);
        Assert.Equal(set, shown);
        Assert.EndsWith("this month, no monthly budget", none, StringComparison.Ordinal);
        Assert.Null((await StateAsync()).Budget);
        Assert.DoesNotContain(service.Logs.Entries, entry => entry.Message.Contains("12.5", StringComparison.Ordinal));
        Assert.Contains(service.Logs.Entries, entry => entry.Message == "The operator changed the monthly AI budget, a budget is set: True");
    }

    [Theory]
    [InlineData("ai", "budget")]
    [InlineData("ai", "budget", "0")]
    [InlineData("ai", "budget", "-5")]
    [InlineData("ai", "budget", "zehn")]
    [InlineData("ai", "budget", "12,50")]
    public async Task Command_WithAWrongBudget_ExitsWith2(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(AiCommand.Invalid, await AiCommand.RunAsync(service.Services, args, output, error));
        Assert.StartsWith("Usage: ai pause|resume|status|budget", error.ToString(), StringComparison.Ordinal);
    }

    // AC3: what the AI cost last month doesn't count, only this UTC month.
    [Fact]
    public async Task OnlyThisMonth_Counts()
    {
        var before = (await StateAsync()).Spent;
        var now = DateTime.UtcNow;
        var lastMonth = AiSwitch.MonthOf(now).AddDays(-1);
        await AiUsageSeed.AddAsync(service.Services, $"alice-{Guid.NewGuid():N}", DateOnly.FromDateTime(lastMonth), (1, 1000m));
        await AiUsageSeed.AddAsync(service.Services, $"alice-{Guid.NewGuid():N}", DateOnly.FromDateTime(now), (1, 0.25m), (2, null));

        Assert.Equal(before + 0.25m, (await StateAsync()).Spent);
    }

    // AC2: the first call that finds 80 % used warns once, the first that finds all of it used says so once, with the
    // markers the platform matches and only the percentage. AC1: from then on the worker asks the AI nothing.
    [Fact]
    public async Task Warning_AndExhausted_GoOutOncePerMonth_AndThenTheAiIsNotAsked()
    {
        await AiUsageSeed.AddAsync(service.Services, $"alice-{Guid.NewGuid():N}", DateOnly.FromDateTime(DateTime.UtcNow), (1, 1m));
        var spent = (await StateAsync()).Spent;
        await ForgetMarksAsync();
        var earlier = service.Logs.Entries.Count;
        IReadOnlyList<TestLogEntry> Since() => [.. service.Logs.Entries.Skip(earlier)];

        await SetBudgetAsync(Math.Round(spent / 0.85m, 6));
        await Chat().GetResponseAsync("Sag ok.", cancellationToken: Cancel);
        await Chat().GetResponseAsync("Sag ok.", cancellationToken: Cancel);

        var warning = Assert.Single(Since(), entry => entry.EventName == "AiBudgetWarning");
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.StartsWith("AI budget: 8", warning.Message, StringComparison.Ordinal);
        Assert.Equal(2, asked);

        await SetBudgetAsync(Math.Round(spent / 2, 6));
        await Assert.ThrowsAsync<AiBudgetExhaustedException>(() => Chat().GetResponseAsync("Sag ok.", cancellationToken: Cancel));
        await Assert.ThrowsAsync<AiBudgetExhaustedException>(() => Chat().GetResponseAsync("Sag ok.", cancellationToken: Cancel));

        var exhausted = Assert.Single(Since(), entry => entry.EventName == "AiBudgetExhausted");
        Assert.Equal(LogLevel.Error, exhausted.Level);
        Assert.StartsWith("AI budget: ", exhausted.Message, StringComparison.Ordinal);
        Assert.Single(Since(), entry => entry.EventName == "AiBudgetWarning");
        Assert.Equal(2, asked);
        Assert.DoesNotContain(Since().Where(entry => entry.EventName?.StartsWith("AiBudget", StringComparison.Ordinal) == true),
            entry => entry.Message.Contains(spent.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    // AC1: used up, a document in analysis fails with a reason the user understands, and the Api turns new jobs away
    // without storing anything and without an amount.
    [Fact]
    public async Task UsedUp_FailsTheAnalysis_AndTheApiTurnsNewJobsAway()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);
        var id = await PasteAsync(client);
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);
        await AiUsageSeed.AddAsync(service.Services, alice, DateOnly.FromDateTime(DateTime.UtcNow), (1, 0.5m));
        await SetBudgetAsync(Math.Round((await StateAsync()).Spent / 2, 6));

        var analyzer = ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, Chat());
        await analyzer.AnalyzeAsync(id, Cancel);

        var failed = await client.GetFromJsonAsync<DocumentDto>($"/documents/{id}", Cancel);
        Assert.Equal(("Failed", Document.AiBudgetReached), (failed!.Status, failed.FailureReason));
        Assert.Equal(0, asked);

        using var pasted = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson, confirmation = "material-1" }, Cancel);
        using var file = new ByteArrayContent("Ein Text mit genug Worten zum Lernen."u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { file, "file", "Notizen.txt" } };
        using var uploaded = await client.PostAsync("/documents/files?confirmation=material-1", form, Cancel);
        using var retried = await client.PostAsync($"/documents/{id}/retry", null, Cancel);

        foreach (var response in new[] { pasted, uploaded, retried })
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Cancel);
            Assert.Equal(Document.AiBudgetReached, problem.GetProperty("title").GetString());
            Assert.False(problem.TryGetProperty("detail", out _));
        }

        Assert.Equal(1, Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == alice)));
        Assert.Equal("Failed", (await client.GetFromJsonAsync<DocumentDto>($"/documents/{id}", Cancel))!.Status);

        await SetBudgetAsync(null);
        using var again = await client.PostAsync($"/documents/{id}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    private async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await AiCommand.RunAsync(service.Services, args, output, error);
        Assert.Empty(error.ToString());
        return (exitCode, output.ToString().Trim());
    }

    private async Task<AiBudgetState> StateAsync()
    {
        using var scope = service.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAiBudget>().StateAsync(Cancel);
    }

    private async Task SetBudgetAsync(decimal? budget)
    {
        using var scope = service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAiBudget>().SetAsync(budget, CancellationToken.None);
    }

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    // The worker's AI: a provider that answers "ok" behind the gate, counting what reaches it.
    private IChatClient Chat() => new AiGate(
        new CountingChatClient(() => asked++),
        service.Services.GetRequiredService<IServiceScopeFactory>(),
        service.Services.GetRequiredService<ILogger<AiGate>>());

    // The marks of this month gone, so a test sees the first call of a month again.
    private async Task ForgetMarksAsync()
    {
        using var scope = service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ContentDbContext>().AiSwitches
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.BudgetWarnedFor, (DateTime?)null)
                .SetProperty(item => item.BudgetExhaustedFor, (DateTime?)null), Cancel);
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Guid> PasteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson, confirmation = "material-1" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Cancel))!.Id;
    }

    private sealed record DocumentDto(Guid Id, string Status, string? FailureReason);

    private sealed class CountingChatClient(Action count) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            count();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}