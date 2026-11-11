using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// Every save that changes the topics of a learning goal stores exactly one TopicsChanged with it, carrying all topics
// of the goal. A redelivered analysis stores none.
public sealed class TopicsChangedTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-DocumentAnalyzedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly string Subject = EventRoute.For(typeof(TopicsChanged)).Subject;

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task Analysis_StoresOneEvent_WithAllTopicsOfTheGoal()
    {
        var alice = NewUser();

        await DeliverAsync(Analysis(alice, "Zelle", "Kern"));

        var changed = Assert.Single(Events(alice));
        Assert.Equal(Guid.Empty, changed.LearningGoalId);
        Assert.Equal(TopicIds(alice).Order(), changed.TopicIds.Order());
        Assert.Equal(2, changed.TopicIds.Count);
        Assert.Empty(changed.HiddenTopicIds);
        Assert.Equal(DateTimeKind.Utc, changed.ChangedAt.Kind);
    }

    // A second document adds topics to the same goal: the new event names the earlier ones too.
    [Fact]
    public async Task SecondAnalysis_StoresOneMoreEvent_WithTheEarlierTopicsToo()
    {
        var alice = NewUser();
        await DeliverAsync(Analysis(alice, "Zelle"));

        await DeliverAsync(Analysis(alice, "Genetik", "Vererbung"));

        var events = Events(alice);
        Assert.Equal(2, events.Count);
        Assert.Equal(TopicIds(alice).Order(), events[1].TopicIds.Order());
        Assert.Equal(3, events[1].TopicIds.Count);
        Assert.True(events[1].ChangedAt > events[0].ChangedAt);
    }

    [Fact]
    public async Task AnalysisDeliveredTwice_OrSentTwice_StoresNoSecondEvent()
    {
        var alice = NewUser();
        var analysis = Analysis(alice, "Zelle");
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await PublishAsync(analysis, messageId);
            await PublishAsync(analysis, messageId);
            var again = await PublishAsync(analysis);

            await WaitForAsync(() => InboxEntries(again) == 1, "The second event was not handled.");
            Assert.Single(Events(alice));
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // Topics from before the default goal join it with the first analysis that names it, in the save of its own
    // topics. The event of the old goal names no topic any more, so the receivers drop them there.
    [Fact]
    public async Task FirstAnalysisWithAGoal_MovesTheTopicsWithoutOne_AndStoresAnEventForEachGoal()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        await DeliverAsync(Analysis(alice, "Zelle", "Kern"));

        await DeliverAsync(Analysis(alice, "Genetik") with { LearningGoalId = goal });

        var events = Events(alice);
        Assert.Equal([Guid.Empty, goal, Guid.Empty], events.Select(changed => changed.LearningGoalId));
        Assert.Equal(TopicIds(alice).Order(), events[1].TopicIds.Order());
        Assert.Equal(3, events[1].TopicIds.Count);
        Assert.Empty(events[2].TopicIds);
        Assert.Equal([goal], Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice).Select(topic => topic.LearningGoalId).Distinct().ToList()));
    }

    [Fact]
    public async Task SecondAnalysisWithAGoal_MovesNothing_AndStoresOnlyTheEventOfItsGoal()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        await DeliverAsync(Analysis(alice, "Zelle"));
        await DeliverAsync(Analysis(alice, "Kern") with { LearningGoalId = goal });

        await DeliverAsync(Analysis(alice, "Genetik") with { LearningGoalId = goal });

        var events = Events(alice);
        Assert.Equal(4, events.Count);
        Assert.Equal(goal, events[3].LearningGoalId);
        Assert.Equal(TopicIds(alice).Order(), events[3].TopicIds.Order());
    }

    [Fact]
    public async Task Rename_StoresOneEvent_WithAllTopicsOfTheGoal()
    {
        var alice = NewUser();
        await DeliverAsync(Analysis(alice, "Zelle", "Kern"));
        var topic = TopicIds(alice)[0];
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(alice));

        using var response = await client.PatchAsJsonAsync($"/topics/{topic}", new { name = "Zellbiologie" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = Events(alice);
        Assert.Equal(2, events.Count);
        Assert.Equal(TopicIds(alice).Order(), events[1].TopicIds.Order());
    }

    [Fact]
    public async Task RejectedRename_StoresNoEvent()
    {
        var alice = NewUser();
        await DeliverAsync(Analysis(alice, "Zelle"));
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(alice));

        using var response = await client.PatchAsJsonAsync($"/topics/{TopicIds(alice)[0]}", new { name = " " }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(Events(alice));
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private static DocumentAnalyzed Analysis(string user, params string[] names) =>
        new(user, Guid.NewGuid(), [.. names.Select(name => new TopicSuggestion(name, null, [Guid.NewGuid()]))]);

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

    // From the outbox, where the event lands in the same save as the change, oldest first.
    private List<TopicsChanged> Events(string user) =>
        [.. Query(db => db.Set<OutboxMessage>().Where(message => message.Subject == Subject).OrderBy(message => message.Id).ToList())
            .Select(message => JsonSerializer.Deserialize<TopicsChanged>(message.Payload, JsonSerializerOptions.Web)!)
            .Where(changed => changed.UserId == user)];

    private List<Guid> TopicIds(string user) =>
        Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == user).OrderBy(topic => topic.Position).Select(topic => topic.Id).ToList());

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<KnowledgeDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>());
    }

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
}