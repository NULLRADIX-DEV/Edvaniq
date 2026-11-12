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

// The whole way of a question pool (#214): Content analyzes a pasted text and sends DocumentAnalyzed, Knowledge gives
// the topics their ids and sends TopicsChanged, Content asks Knowledge for the ids and sends the questions of every
// topic with its id, Assessment takes them over for the topics it knows. Only then is the document ready, and the user
// can practise every topic.
[Collection(FlowCollection.Name)]
public sealed class QuestionFlowTests(FlowServices services) : IDisposable
{
    private const string Text = """
        # Photosynthese
        ## Lichtreaktion
        Licht spaltet Wasser.

        ## Calvin-Zyklus
        CO2 wird zu Zucker.

        # Zellatmung
        Zucker wird zu Energie.
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly IHost ai = SimulatedAi();

    [Fact(Timeout = 180_000)]
    public async Task PastedText_EndsAsQuestionsOfEveryTopicInAssessment()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(alice);
        var documentId = await PasteAsync(token);

        await ActivatorUtilities.CreateInstance<DocumentSplitter>(services.Content.Services).SplitAsync(documentId, TestContext.Current.CancellationToken);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(services.Content.Services, Chat).AnalyzeAsync(documentId, TestContext.Current.CancellationToken);
        Assert.Equal("Analyzing", await StatusAsync(token, documentId));

        // Knowledge takes the topics over in the background, the worker asks until they are there.
        var pools = ActivatorUtilities.CreateInstance<QuestionPools>(services.Content.Services, Chat);
        await WaitForAsync(
            async () => await pools.FinishAsync(documentId, TestContext.Current.CancellationToken),
            "The worker never got the topics from Knowledge.");
        Assert.Equal("Ready", await StatusAsync(token, documentId));

        var topics = services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters()
            .Where(topic => topic.DocumentId == documentId).Select(topic => topic.Id).ToList());
        Assert.Equal(4, topics.Count);
        await FlowServices.WaitForAsync(
            () => Task.FromResult(QuestionTopics(alice).Count == topics.Count),
            () => $"Assessment did not take the questions of every topic over. {services.QuestionsOf(documentId, alice)}",
            Timeout);
        Assert.Equal(topics.Order(), QuestionTopics(alice).Order());

        // Document, topics and every receiver's topics, those with the questions and the plan among them, belong to the
        // default goal that Planning made for the pasted text.
        var goal = Assert.Single(services.QueryPlanning(db => db.LearningGoals.IgnoreQueryFilters()
            .Where(item => item.OwnerId == alice).Select(item => item.Id).ToList()));
        Assert.Equal(goal, services.QueryContent(db => db.Documents.IgnoreQueryFilters().Single(item => item.Id == documentId).LearningGoalId));
        Assert.Equal([goal], services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters()
            .Where(topic => topic.OwnerId == alice).Select(topic => topic.LearningGoalId).Distinct().ToList()));
        foreach (var receiver in services.Receivers)
        {
            await WaitForAsync(
                () => Task.FromResult(receiver.KnownTopics(alice).Count == topics.Count),
                $"{receiver} did not take the topics over.");
            Assert.Equal([goal], receiver.Goals(alice));
        }

        using var assessment = services.Assessment.CreateClient();
        assessment.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var topicId in topics)
        {
            using var asked = await assessment.PostAsJsonAsync("/attempts", new { topicId }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
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

    private async Task<Guid> PasteAsync(string token)
    {
        using var client = ContentClient(token);
        using var response = await client.PostAsJsonAsync("/documents", new { text = Text, confirmation = "material-1" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private async Task<string?> StatusAsync(string token, Guid documentId)
    {
        using var client = ContentClient(token);
        var document = await client.GetFromJsonAsync<JsonElement>($"/documents/{documentId}", TestContext.Current.CancellationToken);
        return document.GetProperty("status").GetString();
    }

    private HttpClient ContentClient(string token)
    {
        var client = services.Content.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // The topics Assessment has questions of.
    private List<Guid> QuestionTopics(string user) =>
        services.QueryAssessment(db => db.Questions.IgnoreQueryFilters()
            .Where(question => question.OwnerId == user && question.TopicId != null)
            .Select(question => question.TopicId!.Value)
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