using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.BuildingBlocks.AI;
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

namespace Edvaniq.Services.Content.IntegrationTests;

// #323: the worker asks Planning for level and output language of the goal right before the AI, so a change of the goal
// counts at the next analysis. When Planning can't say or the goal is gone, the document fails with a reason and the AI
// is never asked, never with defaults.
public sealed class GoalContextTests(ServiceFactory service) : IClassFixture<ServiceFactory>, IDisposable
{
    private readonly IHost ai = SimulatedAi();

    private readonly List<string> systems = [];

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => ai.Dispose();

    [Fact]
    public async Task ChangedGoal_CountsAtTheNextAnalysis()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        service.Planning.Profile(alice, "University", "en");
        using var client = ClientFor(alice);
        var id = await PasteAsync(client);
        await SplitAsync(id);

        // Changed after the text came in, before the analysis.
        service.Planning.Profile(alice, "Apprenticeship", "de");
        await Analyzer().AnalyzeAsync(id, Cancel);

        Assert.Equal([TopicAnalysis.System(LearnerProfile.Of("Apprenticeship", "de"))], systems);
        Assert.Contains(service.Planning.GoalOf(alice), service.Planning.WorkerAsked);
    }

    [Fact]
    public async Task PlanningDown_FailsTheDocumentWithAReason_WithoutAskingTheAi_AndRetryWorks()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);
        var id = await PasteAsync(client);
        await SplitAsync(id);

        service.Planning.Down(alice);
        await Analyzer().AnalyzeAsync(id, Cancel);

        var failed = await client.GetFromJsonAsync<DocumentDto>($"/documents/{id}", Cancel);
        Assert.Equal(("Failed", Document.GoalUnavailable), (failed!.Status, failed.FailureReason));
        Assert.Empty(systems);

        service.Planning.Up(alice);
        using var retry = await client.PostAsync($"/documents/{id}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        await SplitAsync(id);
        await Analyzer().AnalyzeAsync(id, Cancel);

        Assert.NotNull(Stored(id).AnalyzedAt);
        Assert.Equal([TopicAnalysis.System(LearnerProfile.Default)], systems);
    }

    [Fact]
    public async Task GoneGoal_FailsTheDocumentWithAReason_WithoutAskingTheAi()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);
        var id = await PasteAsync(client);
        await SplitAsync(id);

        service.Planning.Forget(service.Planning.GoalOf(alice));
        await Analyzer().AnalyzeAsync(id, Cancel);

        var failed = await client.GetFromJsonAsync<DocumentDto>($"/documents/{id}", Cancel);
        Assert.Equal(("Failed", Document.GoalGone), (failed!.Status, failed.FailureReason));
        Assert.Empty(systems);
    }

    private Document Stored(Guid id)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters().AsNoTracking().Single(item => item.Id == id);
    }

    private Task SplitAsync(Guid id) => ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, Cancel);

    private DocumentAnalyzer Analyzer()
    {
        IChatClient chat = new SystemPrompts(ai.Services.GetRequiredService<IChatClient>(), systems);
        var analyzer = ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, chat);
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
        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson, confirmation = "material-1" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Cancel))!.Id;
    }

    private sealed record DocumentDto(Guid Id, string Status, string? FailureReason);

    // Keeps the system prompt of every request that reaches the AI.
    private sealed class SystemPrompts(IChatClient inner, List<string> systems) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            lock (systems)
            {
                systems.AddRange(messages.Where(message => message.Role == ChatRole.System).Select(message => message.Text));
            }

            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }
}