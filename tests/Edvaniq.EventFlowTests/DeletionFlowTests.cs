using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.EventFlowTests;

// The way of a deleted document (#202): Content deletes it and sends DocumentDeleted, Knowledge drops its sections and
// topics and announces the new state, Assessment drops its questions, and every receiver of TopicsChanged forgets the
// topics. The user's other document keeps everything.
[Collection(FlowCollection.Name)]
public sealed class DeletionFlowTests(FlowServices services) : IDisposable
{
    private const string Deleted = """
        # Photosynthese
        ## Lichtreaktion
        Licht spaltet Wasser.

        # Zellatmung
        Zucker wird zu Energie.
        """;

    private const string Kept = """
        # Genetik
        Gene werden von Eltern an ihre Kinder vererbt.

        # Evolution
        Arten wandeln sich über viele Generationen.
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IHost ai = SimulatedAi();

    [Fact(Timeout = 180_000)]
    public async Task DeletedDocument_LeavesNoTopicOrQuestion_TheOtherDocumentKeepsItsOwn()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(alice);
        var deleted = await ReadyAsync(token, Deleted);
        var kept = await ReadyAsync(token, Kept);
        var keptTopics = TopicsOf(kept);
        await FlowServices.WaitForAsync(
            () => Task.FromResult(QuestionDocuments(alice).Count == 2),
            () => $"Assessment did not take the questions of both documents over. {services.QuestionsOf(deleted, alice)}{Environment.NewLine}{services.QuestionsOf(kept, alice)}",
            Timeout);

        using (var client = ContentClient(token))
        {
            using var response = await client.DeleteAsync($"/documents/{deleted}", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        await WaitForAsync(
            () => Task.FromResult(TopicsOf(deleted).Count == 0 && !QuestionDocuments(alice).Contains(deleted)),
            "Knowledge or Assessment kept something of the deleted document.");
        Assert.Equal(0, services.QueryKnowledge(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.DocumentId == deleted)));
        Assert.Equal(keptTopics.Order(), TopicsOf(kept).Order());
        Assert.Equal([kept], QuestionDocuments(alice));
        foreach (var receiver in services.Receivers)
        {
            await WaitForAsync(
                () => Task.FromResult(receiver.KnownTopics(alice).Count == keptTopics.Count),
                $"{receiver} still knows topics of the deleted document.");
        }
    }

    public void Dispose() => ai.Dispose();

    private IChatClient Chat => ai.Services.GetRequiredService<IChatClient>();

    // Content's AI on the simulated provider.
    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
        builder.AddContentAi();
        return builder.Build();
    }

    // Pastes the text and runs the worker's steps until the document is ready.
    private async Task<Guid> ReadyAsync(string token, string text)
    {
        Guid documentId;
        using (var client = ContentClient(token))
        {
            using var response = await client.PostAsJsonAsync("/documents", new { text }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            documentId = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        }

        await ActivatorUtilities.CreateInstance<DocumentSplitter>(services.Content.Services).SplitAsync(documentId, TestContext.Current.CancellationToken);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(services.Content.Services, Chat).AnalyzeAsync(documentId, TestContext.Current.CancellationToken);
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(services.Content.Services, Chat);
        await WaitForAsync(
            async () => await pools.FinishAsync(documentId, TestContext.Current.CancellationToken),
            "The worker never got the topics from Knowledge.");
        return documentId;
    }

    private HttpClient ContentClient(string token)
    {
        var client = services.Content.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private List<Guid> TopicsOf(Guid documentId) =>
        services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.DocumentId == documentId).Select(topic => topic.Id).ToList());

    // The documents Assessment has questions of.
    private List<Guid> QuestionDocuments(string user) =>
        services.QueryAssessment(db => db.Questions.IgnoreQueryFilters()
            .Where(question => question.OwnerId == user)
            .Select(question => question.DocumentId)
            .Distinct()
            .ToList());

    // Consumers, dispatchers and the worker run in the background.
    private static async Task WaitForAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }
}