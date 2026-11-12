using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// GET /documents/{id}/topics tells Content's worker which ids the topics of its analysis got. Only Content's own internal
// token gets in, and its token gets nowhere else.
public sealed class DocumentTopicTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Content = "content";

    [Fact(Timeout = 60_000)]
    public async Task ContentsToken_GetsTheTopicsOfTheDocument_WithTheirSections_InTheOrderOfTheAnalysis()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var document = Guid.NewGuid();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var cell = Topic.Create(alice, Guid.Empty, "Zelle", Seed.Now, document, 0);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now, document, 1);
        var other = Topic.Create(alice, Guid.Empty, "Genetik", Seed.Now, Guid.NewGuid(), 0);
        await AddAsync([nucleus, other, cell], [cell.AssignSection(document, first), nucleus.AssignSection(document, second)]);
        using var client = ClientWith(TestTokens.CreateForClient(Content));

        var topics = await client.GetFromJsonAsync<DocumentTopic[]>($"/documents/{document}/topics", TestContext.Current.CancellationToken);

        Assert.Equal(
            [new DocumentTopic(cell.Id, null, 0, [first]), new DocumentTopic(nucleus.Id, cell.Id, 1, [second])],
            topics!,
            DocumentTopic.Comparer);
    }

    // #293: the sections hang on the topic's id, so renaming a topic or its subtopic takes them nowhere.
    [Fact(Timeout = 60_000)]
    public async Task RenamedTopics_KeepTheirSections()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var document = Guid.NewGuid();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var cell = Topic.Create(alice, Guid.Empty, "Zelle", Seed.Now, document, 0);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now, document, 1);
        await AddAsync([cell, nucleus], [cell.AssignSection(document, first), nucleus.AssignSection(document, second)]);
        using var user = ClientWith(TestTokens.Create(alice));

        foreach (var (id, name) in new[] { (cell.Id, "Zellbiologie"), (nucleus.Id, "Der Zellkern") })
        {
            using var renamed = await user.PatchAsJsonAsync($"/topics/{id}", new { name }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        }

        using var content = ClientWith(TestTokens.CreateForClient(Content));
        var topics = await content.GetFromJsonAsync<DocumentTopic[]>($"/documents/{document}/topics", TestContext.Current.CancellationToken);
        Assert.Equal(
            [new DocumentTopic(cell.Id, null, 0, [first]), new DocumentTopic(nucleus.Id, cell.Id, 1, [second])],
            topics!,
            DocumentTopic.Comparer);
        using var scope = service.Services.CreateScope();
        var stored = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().TopicSections.IgnoreQueryFilters()
            .Where(section => section.DocumentId == document)
            .Select(section => new { section.TopicId, section.SectionId })
            .ToList()
            .OrderBy(section => section.SectionId == first ? 0 : 1)
            .Select(section => (section.TopicId, section.SectionId));
        Assert.Equal([(cell.Id, first), (nucleus.Id, second)], stored);
    }

    // #208: a later document whose analysis only hangs sections on known topics still has topics, else Content would wait
    // for them in vain. Each topic shows only its sections of the document asked for.
    [Fact(Timeout = 60_000)]
    public async Task DocumentOnKnownTopicsOnly_GetsThoseTopics_WithItsOwnSections()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var (earlier, later) = (Guid.NewGuid(), Guid.NewGuid());
        var (old, fresh) = (Guid.NewGuid(), Guid.NewGuid());
        var cell = Topic.Create(alice, Guid.Empty, "Zelle", Seed.Now, earlier, 0);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now, earlier, 1);
        await AddAsync([cell, nucleus], [cell.AssignSection(earlier, old), nucleus.AssignSection(later, fresh)]);
        using var client = ClientWith(TestTokens.CreateForClient(Content));

        var topics = await client.GetFromJsonAsync<DocumentTopic[]>($"/documents/{later}/topics", TestContext.Current.CancellationToken);

        Assert.Equal([new DocumentTopic(nucleus.Id, cell.Id, 1, [fresh])], topics!, DocumentTopic.Comparer);
    }

    // Hiding can be undone, so Content keeps the questions of a hidden topic: its worker still gets the topic's id.
    [Fact(Timeout = 60_000)]
    public async Task HiddenTopics_StayInTheAnswer()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var document = Guid.NewGuid();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var cell = Topic.Create(alice, Guid.Empty, "Zelle", Seed.Now, document, 0);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now, document, 1);
        cell.Hide();
        await AddAsync([cell, nucleus], [cell.AssignSection(document, first), nucleus.AssignSection(document, second)]);
        using var client = ClientWith(TestTokens.CreateForClient(Content));

        var topics = await client.GetFromJsonAsync<DocumentTopic[]>($"/documents/{document}/topics", TestContext.Current.CancellationToken);

        Assert.Equal(
            [new DocumentTopic(cell.Id, null, 0, [first]), new DocumentTopic(nucleus.Id, cell.Id, 1, [second])],
            topics!,
            DocumentTopic.Comparer);
    }

    // Knowledge hasn't taken the analysis over yet, or the document is someone else's that doesn't exist here.
    [Fact(Timeout = 60_000)]
    public async Task UnknownDocument_IsAnEmptyList()
    {
        using var client = ClientWith(TestTokens.CreateForClient(Content));

        var topics = await client.GetFromJsonAsync<DocumentTopic[]>($"/documents/{Guid.NewGuid()}/topics", TestContext.Current.CancellationToken);

        Assert.Empty(topics!);
    }

    // A user's token and the web host's are for the services' audience, the internal scheme turns them away. Another
    // client with an internal token is no Content.
    [Fact(Timeout = 60_000)]
    public async Task OtherTokens_GetNothing()
    {
        var path = $"/documents/{Guid.NewGuid()}/topics";

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(TestTokens.Create("alice"), path));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(TestTokens.CreateForClient("web", TestTokens.Audience), path));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(TestTokens.CreateForClient(Content, TestTokens.Audience), path));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(TestTokens.CreateForClient("planning"), path));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(TestTokens.Create("alice", audience: TestTokens.InternalAudience), path));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(null, path));
    }

    // Content's token never acts as a user, not even one without data.
    [Fact(Timeout = 60_000)]
    public async Task ContentsToken_IsTurnedAwayEverywhereElse()
    {
        var token = TestTokens.CreateForClient(Content);

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(token, "/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(token, "/topics"));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(token, $"/topics/{Guid.NewGuid()}"));
    }

    private async Task AddAsync(Topic[] topics, TopicSection[] sections)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        db.Topics.AddRange(topics);
        db.TopicSections.AddRange(sections);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<HttpStatusCode> StatusAsync(string? token, string path)
    {
        using var client = ClientWith(token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private HttpClient ClientWith(string? token)
    {
        var client = service.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private sealed record DocumentTopic(Guid Id, Guid? ParentId, int Position, Guid[] SectionIds)
    {
        public static readonly IEqualityComparer<DocumentTopic> Comparer = EqualityComparer<DocumentTopic>.Create(
            (a, b) => a!.Id == b!.Id && a.ParentId == b.ParentId && a.Position == b.Position && a.SectionIds.SequenceEqual(b.SectionIds),
            topic => topic.Id.GetHashCode());
    }
}