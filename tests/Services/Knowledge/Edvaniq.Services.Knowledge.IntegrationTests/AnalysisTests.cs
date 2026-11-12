using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// DocumentAnalyzed from Content brings the user's topics: two levels in the order of the analysis, with their sections,
// once per document, seen by nobody else.
public sealed class AnalysisTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-DocumentAnalyzedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task AnalyzedDocument_ShowsItsTopicsInTwoLevels_InTheOrderOfTheAnalysis()
    {
        var alice = NewUser("alice");

        await DeliverAsync(Analysis(alice));

        using var client = ClientFor(alice);
        var trees = await ListAsync(client);
        Assert.Equal(["Zelle", "Genetik"], trees.Select(tree => tree.Name));
        Assert.Equal(["Zellkern", "Membran"], trees[0].Subtopics.Select(subtopic => subtopic.Name));
        Assert.Equal(["Vererbung"], trees[1].Subtopics.Select(subtopic => subtopic.Name));
        Assert.All(trees, tree => Assert.Equal(Guid.Empty, tree.LearningGoalId));
    }

    [Fact]
    public async Task AnalyzedDocument_KeepsTheSectionsOfEachTopic()
    {
        var alice = NewUser("alice");
        var analysis = Analysis(alice);

        await DeliverAsync(analysis);

        var expected = analysis.Topics.SelectMany(topic => topic.SectionIds.Select(section => (topic.Name, section))).Order();
        var stored = Query(db =>
            (from section in db.TopicSections.IgnoreQueryFilters()
             join topic in db.Topics.IgnoreQueryFilters() on section.TopicId equals topic.Id
             where section.OwnerId == alice
             select new { topic.Name, section.SectionId }).ToList());
        Assert.Equal(expected, stored.Select(row => (row.Name, row.SectionId)).Order());
    }

    // A redelivery is dropped by the inbox, a second event for the same document by the handler.
    [Fact]
    public async Task AnalysisDeliveredTwice_OrSentTwice_AddsNoTopicTwice()
    {
        var alice = NewUser("alice");
        var analysis = Analysis(alice);
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(analysis, messageId);
            await PublishAsync(analysis, messageId);
            var again = await PublishAsync(analysis);

            // One consumer, in order: once the second event is through, so is the duplicate.
            await WaitForAsync(() => InboxEntries(again) == 1, "The second event was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Equal((5, 6), Rows(alice));
            using var client = ClientFor(alice);
            var trees = await ListAsync(client);
            Assert.Equal(["Zelle", "Genetik"], trees.Select(tree => tree.Name));
            Assert.Equal(["Zellkern", "Membran"], trees[0].Subtopics.Select(subtopic => subtopic.Name));
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // The same document id under another user is no duplicate: the check names the owner.
    [Fact]
    public async Task TopicsOfAnotherUser_AreNotVisible()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var ofAlice = Analysis(alice);
        await DeliverAsync(ofAlice);
        await DeliverAsync(ofAlice with { UserId = bob, Topics = [new TopicSuggestion("Algebra", null, [Guid.NewGuid()])] });

        using var asBob = ClientFor(bob);
        Assert.Equal(["Algebra"], (await ListAsync(asBob)).Select(tree => tree.Name));
        using var asAlice = ClientFor(alice);
        var ofAliceListed = await ListAsync(asAlice);
        Assert.Equal(["Zelle", "Genetik"], ofAliceListed.Select(tree => tree.Name));
        using var response = await asBob.GetAsync($"/topics/{ofAliceListed[0].Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletedUser_LosesTheTopicsAndSectionsOfTheAnalysis()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await DeliverAsync(Analysis(alice));
        await DeliverAsync(Analysis(bob));

        var deletion = Guid.NewGuid();
        await Broker.PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(UserDeleted)).Subject,
                deletion,
                JsonSerializer.Serialize(new UserDeleted(alice), JsonSerializerOptions.Web),
                null),
            TestContext.Current.CancellationToken);

        await WaitForAsync(
            () => Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == deletion)) == 1,
            "The deletion was not handled.");
        Assert.Equal((0, 0), Rows(alice));
        Assert.Equal((5, 6), Rows(bob));
    }

    // #944: an analysis that comes after the user's deletion is confirmed and stores nothing, no TopicsChanged either.
    [Fact]
    public async Task LateAnalysis_OfADeletedUser_IsConfirmedWithoutEffect()
    {
        var alice = NewUser("alice");
        var deletion = Guid.NewGuid();
        await Broker.PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(UserDeleted)).Subject,
                deletion,
                JsonSerializer.Serialize(new UserDeleted(alice), JsonSerializerOptions.Web),
                null),
            TestContext.Current.CancellationToken);
        await WaitForAsync(
            () => Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == deletion)) == 1,
            "The deletion was not handled.");

        var late = await PublishAsync(Analysis(alice));

        await WaitForAsync(() => InboxEntries(late) == 1, "The late analysis was not confirmed.");
        Assert.Equal((0, 0), Rows(alice));
        Assert.Equal(0, Query(db => db.Set<OutboxMessage>().Count(message => message.Payload.Contains(alice))));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    // Names out of alphabetical order, so the list shows the order of the analysis.
    private static DocumentAnalyzed Analysis(string user) =>
        new(user, Guid.NewGuid(),
        [
            new("Zelle", null, [Guid.NewGuid()]),
            new("Zellkern", 0, [Guid.NewGuid(), Guid.NewGuid()]),
            new("Membran", 0, [Guid.NewGuid()]),
            new("Genetik", null, [Guid.NewGuid()]),
            new("Vererbung", 3, [Guid.NewGuid()]),
        ]);

    private async Task DeliverAsync(DocumentAnalyzed analysis)
    {
        var messageId = await PublishAsync(analysis);
        await WaitForAsync(() => InboxEntries(messageId) == 1, "The analysis was not handled.");
    }

    // Like Content's dispatcher would.
    private async Task<Guid> PublishAsync(DocumentAnalyzed analysis, Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();
        var payload = JsonSerializer.Serialize(analysis, JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(DocumentAnalyzed)).Subject, id, payload, null),
            TestContext.Current.CancellationToken);
        return id;
    }

    private (int Topics, int Sections) Rows(string user) =>
        (Query(db => db.Topics.IgnoreQueryFilters().Count(topic => topic.OwnerId == user)),
         Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.OwnerId == user)));

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<KnowledgeDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>());
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Tree[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Tree[]>("/topics", TestContext.Current.CancellationToken))!;

    // The consumer runs in the background.
    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private sealed record Tree(Guid Id, Guid LearningGoalId, string Name, Leaf[] Subtopics);

    private sealed record Leaf(Guid Id, string Name);
}