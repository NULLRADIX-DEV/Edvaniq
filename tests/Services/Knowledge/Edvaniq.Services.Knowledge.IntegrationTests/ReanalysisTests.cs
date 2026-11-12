using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// #327: what the user changed survives every later analysis, of the same document (a retry, with new sections) and of
// another one. Renamed keeps the user's name, hidden stays hidden and comes back nowhere, added stays, and nothing is
// there twice. All through DocumentAnalyzed as Content sends it.
public sealed class ReanalysisTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-DocumentAnalyzedHandler";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserChanges_SurviveAnalysesOfTheSameAndAnotherDocument()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var goal = Guid.NewGuid();
        var document = Guid.NewGuid();
        using var client = ClientFor(alice);
        await AnalyzeAsync(alice, goal, document, ["Zelle", "Genetik", "Evolution"], subtopicOfFirst: "Zellkern");
        var before = await TreeAsync(client, goal);

        // The user renames one, hides one and adds one of their own.
        var cell = before.Single(topic => topic.Name == "Zelle");
        var genetics = before.Single(topic => topic.Name == "Genetik");
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/topics/{cell.Id}", new { name = "Die Zelle" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/topics/{genetics.Id}/hidden", null, Cancel)).StatusCode);
        using (var added = await client.PostAsJsonAsync("/topics", new { name = "Mein Lernplan", learningGoalId = goal }, Cancel))
        {
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        }

        var edited = await TreeAsync(client, goal);

        // The same document once more, with new sections as after a retry, then another document.
        await AnalyzeAsync(alice, goal, document, ["Zelle", "Genetik", "Evolution"], subtopicOfFirst: "Zellkern");
        await AnalyzeAsync(alice, goal, Guid.NewGuid(), ["Zelle", "Genetik", "Mein Lernplan", "Ökologie"], subtopicOfFirst: "Zellkern");

        var after = await TreeAsync(client, goal);
        Assert.Equal(
            ["Die Zelle", "Evolution", "Genetik", "Mein Lernplan", "Ökologie"],
            after.Select(topic => topic.Name).Order(StringComparer.Ordinal));
        Assert.Equal((cell.Id, false), Single(after, "Die Zelle"));
        Assert.Equal((genetics.Id, true), Single(after, "Genetik"));
        Assert.Equal(edited.Single(topic => topic.Name == "Mein Lernplan").Id, Single(after, "Mein Lernplan").Id);
        Assert.Equal(["Zellkern"], after.Single(topic => topic.Name == "Die Zelle").Subtopics.Select(subtopic => subtopic.Name));

        // Every topic that was there before keeps its id, the analyses only added "Ökologie".
        Assert.Equal(
            edited.SelectMany(Ids).Order(),
            after.Where(topic => topic.Name != "Ökologie").SelectMany(Ids).Order());

        // Hidden stays hidden for the list without hidden ones too.
        var visible = await client.GetFromJsonAsync<Tree[]>($"/topics?learningGoalId={goal}", Cancel);
        Assert.DoesNotContain(visible!, topic => topic.Name == "Genetik");
    }

    private static (Guid Id, bool Hidden) Single(Tree[] topics, string name)
    {
        var topic = Assert.Single(topics, topic => topic.Name == name);
        return (topic.Id, topic.Hidden);
    }

    private static IEnumerable<Guid> Ids(Tree topic) => topic.Subtopics.Select(subtopic => subtopic.Id).Prepend(topic.Id);

    private static async Task<Tree[]> TreeAsync(HttpClient client, Guid goal) =>
        (await client.GetFromJsonAsync<Tree[]>($"/topics?learningGoalId={goal}&includeHidden=true", Cancel))!;

    // Like Content's dispatcher would, each topic with a new section, the subtopic under the first one. Waits until the
    // handler confirmed it: the same document again replaces its sections, so their count says nothing.
    private async Task AnalyzeAsync(string user, Guid goal, Guid document, IReadOnlyList<string> names, string subtopicOfFirst)
    {
        List<TopicSuggestion> topics = [.. names.Select(name => new TopicSuggestion(name, null, [Guid.NewGuid()]))];
        topics.Insert(1, new TopicSuggestion(subtopicOfFirst, 0, [Guid.NewGuid()]));
        var messageId = Guid.NewGuid();
        await service.Services.GetRequiredService<InMemoryTransport>().PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(DocumentAnalyzed)).Subject,
                messageId,
                JsonSerializer.Serialize(new DocumentAnalyzed(user, document, topics, goal), JsonSerializerOptions.Web),
                null),
            Cancel);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer)) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The analysis was not handled.");
            await Task.Delay(100, Cancel);
        }
    }

    private T Query<T>(Func<KnowledgeDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>());
    }

    private HttpClient ClientFor(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private sealed record Tree(Guid Id, string Name, bool Hidden, Leaf[] Subtopics);

    private sealed record Leaf(Guid Id, string Name, bool Hidden);
}