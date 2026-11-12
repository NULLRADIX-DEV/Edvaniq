using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.EventFlowTests;

// The whole way of TopicsChanged over NATS: Knowledge takes topics over from an analysis or renames one, stores the
// event with the change, its dispatcher sends it, and Planning, Assessment and LearningEngine each know the topics after.
[Collection(FlowCollection.Name)]
public sealed class TopicFlowTests(FlowServices services)
{
    // The receivers may wait a few seconds for Knowledge's stream before the first event.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly string Subject = EventRoute.For(typeof(TopicsChanged)).Subject;

    [Fact]
    public async Task NewAndRenamedTopics_ReachEveryReceiver()
    {
        var alice = NewUser("alice");

        await AnalyseAsync(alice, "Zelle", "Kern");

        var topics = await WaitForTopicsAsync(alice, 2);
        var created = (await WaitForEventsAsync(alice, 1))[0];
        Assert.Equal(topics.Order(), created.TopicIds.Order());
        await WaitForEveryReceiverAsync(alice, topics, created.ChangedAt);

        using var client = services.Knowledge.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(alice));
        using var response = await client.PatchAsJsonAsync($"/topics/{topics[0]}", new { name = "Zellbiologie" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var renamed = (await WaitForEventsAsync(alice, 2))[1];
        Assert.Equal(topics.Order(), renamed.TopicIds.Order());
        await WaitForEveryReceiverAsync(alice, topics, renamed.ChangedAt);
    }

    // The newest state again under a new message id, and the older state after it: no receiver changes.
    [Fact]
    public async Task SameStateAgain_OrAnOlderOne_ChangesNoReceiver()
    {
        var bob = NewUser("bob");
        await AnalyseAsync(bob, "Zelle");
        await WaitForEventsAsync(bob, 1);
        await AnalyseAsync(bob, "Genetik");
        var topics = await WaitForTopicsAsync(bob, 2);
        var events = await WaitForEventsAsync(bob, 2);
        var (older, newer) = (events[0], events[1]);
        await WaitForEveryReceiverAsync(bob, topics, newer.ChangedAt);

        var again = await PublishAsync(Subject, newer);
        var late = await PublishAsync(Subject, older);

        foreach (var receiver in services.Receivers)
        {
            // One consumer, in order: once the older state is through, so is the repeated one.
            await WaitForAsync(() => receiver.InboxEntries(late) == 1, $"{receiver} did not handle the older state.");
            Assert.Equal(1, receiver.InboxEntries(again));
            Assert.Equal(topics.Order(), receiver.KnownTopics(bob).Order());
            Assert.Equal(newer.ChangedAt, receiver.ChangedAt(bob));
        }
    }

    // Topics from before the default goal move to it with the first analysis that names it. Every receiver ends with
    // all topics in that goal, whichever of the two events it handles first.
    [Fact]
    public async Task TopicsWithoutAGoal_MoveToTheFirstGoal_AtEveryReceiver()
    {
        var carol = NewUser("carol");
        var goal = Guid.NewGuid();
        await AnalyseAsync(carol, "Zelle");
        await WaitForEventsAsync(carol, 1);

        await AnalyseAsync(carol, goal, ["Genetik"]);

        var topics = await WaitForTopicsAsync(carol, 2);
        var events = await WaitForEventsAsync(carol, 3);
        Assert.Equal([Guid.Empty, goal, Guid.Empty], events.Select(changed => changed.LearningGoalId));
        foreach (var receiver in services.Receivers)
        {
            await WaitForAsync(
                () => receiver.Goals(carol) is [var only] && only == goal && receiver.KnownTopics(carol).Count == topics.Count,
                $"{receiver} did not move the topics to the goal.");
            Assert.Equal(topics.Order(), receiver.KnownTopics(carol).Order());
        }
    }

    // #380 AC1: after the analysis Planning knows each topic's place in the material and its size, the number of its
    // sections, for the user of the analysis.
    [Fact]
    public async Task Analysis_GivesPlanningTheOrderAndSizeOfTheTopics()
    {
        var carol = NewUser("carol");
        await services.Broker.EnsureStreamAsync(
            new StreamSpec(EventRoute.StreamOf("content"), EventRoute.SubjectsOf("content")),
            TestContext.Current.CancellationToken);

        await PublishAsync(
            EventRoute.For(typeof(DocumentAnalyzed)).Subject,
            new DocumentAnalyzed(carol, Guid.NewGuid(), [
                new TopicSuggestion("Zelle", null, [Guid.NewGuid(), Guid.NewGuid()]),
                new TopicSuggestion("Gewebe", null, [Guid.NewGuid()]),
                new TopicSuggestion("Kern", 0, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()])]));

        await WaitForTopicsAsync(carol, 3);
        var changed = (await WaitForEventsAsync(carol, 1))[0];
        await WaitForAsync(
            () => services.QueryPlanning(db => db.TopicsSnapshots.IgnoreQueryFilters().Any(snapshot => snapshot.OwnerId == carol && snapshot.ChangedAt == changed.ChangedAt)),
            "Planning did not take the state over.");
        var names = services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == carol).ToDictionary(topic => topic.Id, topic => topic.Name));
        var known = services.QueryPlanning(db => db.KnownTopics.IgnoreQueryFilters()
            .Where(topic => topic.OwnerId == carol)
            .Select(topic => new { topic.TopicId, topic.Rank, topic.Size })
            .ToList());

        Assert.Equal(
            [("Zelle", 0, 2), ("Kern", 1, 3), ("Gewebe", 2, 1)],
            known.OrderBy(topic => topic.Rank).Select(topic => (names[topic.TopicId], topic.Rank!.Value, topic.Size)));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private Task AnalyseAsync(string user, params string[] names) => AnalyseAsync(user, null, names);

    // Like Content's dispatcher would: into Content's stream.
    private async Task AnalyseAsync(string user, Guid? learningGoalId, string[] names)
    {
        await services.Broker.EnsureStreamAsync(
            new StreamSpec(EventRoute.StreamOf("content"), EventRoute.SubjectsOf("content")),
            TestContext.Current.CancellationToken);
        await PublishAsync(
            EventRoute.For(typeof(DocumentAnalyzed)).Subject,
            new DocumentAnalyzed(user, Guid.NewGuid(), [.. names.Select(name => new TopicSuggestion(name, null, [Guid.NewGuid()]))], learningGoalId));
    }

    private async Task<Guid> PublishAsync<TEvent>(string subject, TEvent integrationEvent)
    {
        var messageId = Guid.NewGuid();
        await services.Broker.PublishAsync(
            new OutgoingMessage(subject, messageId, JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web)),
            TestContext.Current.CancellationToken);
        return messageId;
    }

    private async Task<List<Guid>> WaitForTopicsAsync(string user, int count)
    {
        List<Guid> TopicIds() => services.QueryKnowledge(db =>
            db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).Select(topic => topic.Id).ToList());

        await WaitForAsync(() => TopicIds().Count == count, "Knowledge did not take the topics over.");
        return TopicIds();
    }

    // From Knowledge's outbox, oldest first, once the dispatcher has sent them.
    private async Task<List<TopicsChanged>> WaitForEventsAsync(string user, int count)
    {
        List<TopicsChanged> Sent() =>
            [.. services.QueryKnowledge(db => db.Set<OutboxMessage>()
                    .Where(message => message.Subject == Subject && message.SentAt != null)
                    .OrderBy(message => message.Id)
                    .Select(message => message.Payload)
                    .ToList())
                .Select(payload => JsonSerializer.Deserialize<TopicsChanged>(payload, JsonSerializerOptions.Web)!)
                .Where(changed => changed.UserId == user)];

        await WaitForAsync(() => Sent().Count >= count, $"Knowledge did not send {count} TopicsChanged.");
        var sent = Sent();
        Assert.Equal(count, sent.Count);
        return sent;
    }

    private async Task WaitForEveryReceiverAsync(string user, List<Guid> topics, DateTime changedAt)
    {
        foreach (var receiver in services.Receivers)
        {
            await WaitForAsync(() => receiver.ChangedAt(user) == changedAt, $"{receiver} did not take the state over.");
            Assert.Equal(topics.Order(), receiver.KnownTopics(user).Order());
        }
    }

    // Consumers and dispatchers run in the background.
    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }
}