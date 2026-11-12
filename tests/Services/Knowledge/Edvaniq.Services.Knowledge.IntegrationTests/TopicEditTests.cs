using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// The user adds topics on two levels, hides them with their subtopics and brings them back, all without an id changing.
// Every change stores exactly one TopicsChanged, one that changes nothing or is turned away none.
public sealed class TopicEditTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly string Subject = EventRoute.For(typeof(TopicsChanged)).Subject;

    [Fact]
    public async Task Add_MakesATopicAndASubtopic_InTheGoal()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        using var client = ClientFor(alice);

        var topic = await AddAsync(client, new { name = " Zelle ", learningGoalId = goal });
        var subtopic = await AddAsync(client, new { name = "Zellkern", parentId = topic.Id });

        Assert.Equal(new TopicDto(topic.Id, goal, null, "Zelle", false), topic);
        Assert.Equal(new TopicDto(subtopic.Id, goal, topic.Id, "Zellkern", false), subtopic);
        var tree = Assert.Single(await ListAsync(client, $"/topics?learningGoalId={goal}"));
        Assert.Equal(subtopic.Id, Assert.Single(tree.Subtopics).Id);

        var events = Events(alice);
        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { topic.Id, subtopic.Id }.Order(), events[1].TopicIds.Order());
        Assert.Equal(new TopicParent(subtopic.Id, topic.Id), Assert.Single(events[1].Parents!));
    }

    [Theory]
    [InlineData("""{ "name": " ", "learningGoalId": "6f1d8a52-6f5e-4a54-9c8e-6a6f3c1b2d10" }""", "Name")]
    [InlineData("""{ "name": "Zelle" }""", "LearningGoalId")]
    [InlineData("""{ "name": "Zelle", "learningGoalId": "00000000-0000-0000-0000-000000000000" }""", "LearningGoalId")]
    [InlineData("""{ "name": "Zelle", "learningGoalId": "6f1d8a52-6f5e-4a54-9c8e-6a6f3c1b2d10", "parentId": "6f1d8a52-6f5e-4a54-9c8e-6a6f3c1b2d11" }""", "LearningGoalId")]
    public async Task Add_WithoutANameOrWithoutExactlyOneGoalOrParent_IsABadRequest(string body, string field)
    {
        var alice = NewUser();
        using var client = ClientFor(alice);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/topics", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(Events(alice));
    }

    // Topics have two levels.
    [Fact]
    public async Task Add_UnderASubtopic_IsABadRequest()
    {
        var alice = NewUser();
        var cell = Topic.Create(alice, Guid.NewGuid(), "Zelle", Seed.Now);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now);
        await service.AddAsync(cell, nucleus);
        using var client = ClientFor(alice);

        using var response = await client.PostAsJsonAsync("/topics", new { name = "Chromatin", parentId = nucleus.Id }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(nucleus.Id, Assert.Single(Assert.Single(await ListAllAsync(client)).Subtopics).Id);
    }

    // A name a sibling has, after TopicName.Normalize, also a hidden sibling's. The answer names no other topic.
    [Fact]
    public async Task AddOrRename_ToTheNameOfASibling_IsAConflict_EvenIfTheSiblingIsHidden()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        var cell = Topic.Create(alice, goal, "Zelle", Seed.Now);
        var genetics = Topic.Create(alice, goal, "Genetik", Seed.Now);
        var hidden = Topic.Create(alice, goal, "Ökologie", Seed.Now);
        hidden.Hide();
        await service.AddAsync(cell, genetics, hidden);
        using var client = ClientFor(alice);

        using var added = await client.PostAsJsonAsync("/topics", new { name = "zelle", learningGoalId = goal }, TestContext.Current.CancellationToken);
        using var renamed = await client.PatchAsJsonAsync($"/topics/{genetics.Id}", new { name = "Zelle" }, TestContext.Current.CancellationToken);
        using var toHidden = await client.PatchAsJsonAsync($"/topics/{genetics.Id}", new { name = "Oekologie" }, TestContext.Current.CancellationToken);
        using var addedHidden = await client.PostAsJsonAsync("/topics", new { name = "ökologie", learningGoalId = goal }, TestContext.Current.CancellationToken);

        foreach (var response in new[] { added, renamed, toHidden, addedHidden })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain(cell.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(hidden.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("Genetik", (await GetAsync(client, genetics.Id)).Name);
        Assert.Empty(Events(alice));
    }

    // Same name on another level, under another topic, in another goal or of another user is fine.
    [Fact]
    public async Task SameName_ElsewhereThanAmongSiblings_IsFine()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        var cell = Topic.Create(alice, goal, "Zelle", Seed.Now);
        await service.AddAsync(cell, Topic.Create(NewUser(), goal, "Genetik", Seed.Now));
        using var client = ClientFor(alice);

        await AddAsync(client, new { name = "Zelle", parentId = cell.Id });
        await AddAsync(client, new { name = "Zelle", learningGoalId = Guid.NewGuid() });
        await AddAsync(client, new { name = "Genetik", learningGoalId = goal });
    }

    [Fact]
    public async Task Hide_TakesTheSubtopicsAlong_AndShow_BringsThemBackInTheirOwnState()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        var cell = Topic.Create(alice, goal, "Zelle", Seed.Now);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now.AddSeconds(1));
        var mitochondria = cell.AddSubtopic("Mitochondrien", Seed.Now.AddSeconds(2));
        var genetics = Topic.Create(alice, goal, "Genetik", Seed.Now.AddSeconds(3));
        await service.AddAsync(cell, nucleus, mitochondria, genetics);
        using var client = ClientFor(alice);

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, HttpMethod.Put, $"/topics/{mitochondria.Id}/hidden"));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, HttpMethod.Put, $"/topics/{cell.Id}/hidden"));

        Assert.Equal([genetics.Id], (await ListAsync(client, "/topics")).Select(tree => tree.Id));
        var all = await ListAllAsync(client);
        Assert.Equal([cell.Id, genetics.Id], all.Select(tree => tree.Id));
        Assert.Equal([new Leaf(nucleus.Id, "Zellkern", true), new Leaf(mitochondria.Id, "Mitochondrien", true)], all[0].Subtopics);
        Assert.True((await GetAsync(client, nucleus.Id)).Hidden);

        // A subtopic comes back only with its topic.
        using var early = await client.DeleteAsync($"/topics/{nucleus.Id}/hidden", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Assert.Equal(HttpStatusCode.OK, await SendAsync(client, HttpMethod.Delete, $"/topics/{cell.Id}/hidden"));
        var tree = (await ListAsync(client, "/topics"))[0];
        Assert.Equal((cell.Id, false), (tree.Id, tree.Hidden));
        Assert.Equal([new Leaf(nucleus.Id, "Zellkern", false)], tree.Subtopics);
    }

    [Fact]
    public async Task EveryHideOrShow_StoresOneEvent_WithTheHiddenOnesApart_AndARepeatStoresNone()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        var cell = Topic.Create(alice, goal, "Zelle", Seed.Now);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now);
        var genetics = Topic.Create(alice, goal, "Genetik", Seed.Now);
        await service.AddAsync(cell, nucleus, genetics);
        using var client = ClientFor(alice);

        await SendAsync(client, HttpMethod.Put, $"/topics/{cell.Id}/hidden");
        await SendAsync(client, HttpMethod.Put, $"/topics/{cell.Id}/hidden");

        var hidden = Assert.Single(Events(alice));
        Assert.Equal(goal, hidden.LearningGoalId);
        Assert.Equal([genetics.Id], hidden.TopicIds);
        Assert.Equal(new[] { cell.Id, nucleus.Id }.Order(), hidden.HiddenTopicIds.Order());
        Assert.Equal(new TopicParent(nucleus.Id, cell.Id), Assert.Single(hidden.Parents!));

        await SendAsync(client, HttpMethod.Delete, $"/topics/{cell.Id}/hidden");
        await SendAsync(client, HttpMethod.Delete, $"/topics/{cell.Id}/hidden");

        var events = Events(alice);
        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { cell.Id, nucleus.Id, genetics.Id }.Order(), events[1].TopicIds.Order());
        Assert.Empty(events[1].HiddenTopicIds);
        Assert.True(events[1].ChangedAt > hidden.ChangedAt);
    }

    // An analysis that brings a hidden topic again leaves it hidden and adds no second one.
    [Fact]
    public async Task HiddenTopic_StaysHidden_WhenAnAnalysisBringsItAgain()
    {
        var alice = NewUser();
        var hidden = Topic.Create(alice, Guid.Empty, "Zelle", Seed.Now);
        hidden.Hide();
        await service.AddAsync(hidden);

        await service.Services.GetRequiredService<InMemoryTransport>().PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(DocumentAnalyzed)).Subject,
                Guid.NewGuid(),
                JsonSerializer.Serialize(
                    new DocumentAnalyzed(alice, Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()])]),
                    JsonSerializerOptions.Web),
                null),
            TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.OwnerId == alice)) == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The analysis was not handled.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var stored = Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice).ToList());
        Assert.Equal((hidden.Id, true), (Assert.Single(stored).Id, stored[0].Hidden));
    }

    // #317: topics the user entered keep their ids and names when analyses bring them later, under another spelling too.
    // The analyses only add their sections and their new topics, a second analysis no more than the first.
    [Fact]
    public async Task EnteredTopics_AreReusedByLaterAnalyses_WhichOnlyAdd()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        using var client = ClientFor(alice);
        var photosynthesis = await AddAsync(client, new { name = "Fotosynthese", learningGoalId = goal });
        var lightReaction = await AddAsync(client, new { name = "Lichtreaktion", parentId = photosynthesis.Id });

        await AnalyzeAsync(alice, goal, [new("FOTOSYNTHESE", null, [Guid.NewGuid()]), new("lichtreaktion", 0, [Guid.NewGuid()]), new("Zellatmung", null, [Guid.NewGuid()])]);
        var afterFirst = Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice).ToList());
        await AnalyzeAsync(alice, goal, [new("Fotosynthese", null, [Guid.NewGuid()]), new("Zellatmung", null, [Guid.NewGuid()])]);

        var topics = Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice).ToList()).ToDictionary(topic => topic.Name);
        Assert.Equal(["Fotosynthese", "Lichtreaktion", "Zellatmung"], topics.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((photosynthesis.Id, (Guid?)null), (topics["Fotosynthese"].Id, topics["Fotosynthese"].ParentId));
        Assert.Equal((lightReaction.Id, (Guid?)photosynthesis.Id), (topics["Lichtreaktion"].Id, topics["Lichtreaktion"].ParentId));
        Assert.Null(topics["Zellatmung"].ParentId);
        Assert.Equal(afterFirst.Select(topic => topic.Id).Order(), topics.Values.Select(topic => topic.Id).Order());
        Assert.Equal(2, Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.TopicId == photosynthesis.Id)));
    }

    // Like Content's dispatcher would, and waits until the sections are there.
    private async Task AnalyzeAsync(string user, Guid goal, IReadOnlyList<TopicSuggestion> topics)
    {
        var sections = topics.Sum(topic => topic.SectionIds.Count) + Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.OwnerId == user));
        await service.Services.GetRequiredService<InMemoryTransport>().PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(DocumentAnalyzed)).Subject,
                Guid.NewGuid(),
                JsonSerializer.Serialize(new DocumentAnalyzed(user, Guid.NewGuid(), topics, goal), JsonSerializerOptions.Web),
                null),
            TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Query(db => db.TopicSections.IgnoreQueryFilters().Count(section => section.OwnerId == user)) < sections)
        {
            Assert.True(DateTime.UtcNow < deadline, "The analysis was not handled.");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<TopicDto> AddAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/topics", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TopicDto>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<TopicDto> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<TopicDto>($"/topics/{id}", TestContext.Current.CancellationToken))!;

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(method, path), TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<Tree[]> ListAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<Tree[]>(path, TestContext.Current.CancellationToken))!;

    private static Task<Tree[]> ListAllAsync(HttpClient client) => ListAsync(client, "/topics?includeHidden=true");

    // From the outbox, where the event lands in the same save as the change, oldest first.
    private List<TopicsChanged> Events(string user) =>
        [.. Query(db => db.Set<OutboxMessage>().Where(message => message.Subject == Subject).OrderBy(message => message.Id).ToList())
            .Select(message => JsonSerializer.Deserialize<TopicsChanged>(message.Payload, JsonSerializerOptions.Web)!)
            .Where(changed => changed.UserId == user)];

    private T Query<T>(Func<KnowledgeDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>());
    }

    private sealed record TopicDto(Guid Id, Guid LearningGoalId, Guid? ParentId, string Name, bool Hidden);

    private sealed record Tree(Guid Id, string Name, bool Hidden, Leaf[] Subtopics);

    private sealed record Leaf(Guid Id, string Name, bool Hidden);
}