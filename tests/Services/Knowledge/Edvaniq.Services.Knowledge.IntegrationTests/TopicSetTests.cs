using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// #322: two changes of one learning goal at once don't pass each other. The second save finds the topics changed and
// tries again on the new state, as the broker's redelivery does for a handler. The tests interleave the two by hand.
public sealed class TopicSetTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Both analyses read the goal before either saves. The second save loses, its redelivery merges into the topics the
    // first one made: each name once, the sections of both documents on the same topics.
    [Fact]
    public async Task TwoAnalysesAtOnce_LeaveOneTopicPerName()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        var first = Analysis(alice, goal);
        var second = Analysis(alice, goal);

        using var one = service.Services.CreateScope();
        using var other = service.Services.CreateScope();
        await HandlerIn(one).HandleAsync(first, Cancel);
        await HandlerIn(other).HandleAsync(second, Cancel);
        await DbIn(one).SaveChangesAsync(Cancel);
        await Assert.ThrowsAsync<MessageNotReadyException>(() => DbIn(other).SaveChangesAsync(Cancel));
        await HandleAgainAsync(second);

        var topics = Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice).ToList());
        Assert.Equal(["Membran", "Zelle", "Zellkern"], topics.Select(topic => topic.Name).Order());
        var documents = Query(db => db.TopicSections.IgnoreQueryFilters().Where(section => section.OwnerId == alice)
            .Select(section => section.DocumentId).Distinct().ToList());
        Assert.Equal(new[] { first.DocumentId, second.DocumentId }.Order(), documents.Order());
    }

    // The analysis read the goal, then the user renamed a topic. The analysis loses, tries again, and hangs its new
    // subtopic under the renamed topic: the new name stays, no topic of the old name comes back.
    [Fact]
    public async Task RenameDuringAnAnalysis_IsNotLost()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        await HandleAgainAsync(Analysis(alice, goal));
        var cell = Query(db => db.Topics.IgnoreQueryFilters().Single(topic => topic.OwnerId == alice && topic.Name == "Zelle"));
        var later = new DocumentAnalyzed(
            alice, Guid.NewGuid(), [new("Zelle", null, [Guid.NewGuid()]), new("Mitochondrien", 0, [Guid.NewGuid()])], goal);

        using var analysis = service.Services.CreateScope();
        await HandlerIn(analysis).HandleAsync(later, Cancel);
        using var client = ClientFor(alice);
        using var renamed = await client.PatchAsJsonAsync($"/topics/{cell.Id}", new { name = "Zellbiologie" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        await Assert.ThrowsAsync<MessageNotReadyException>(() => DbIn(analysis).SaveChangesAsync(Cancel));
        await HandleAgainAsync(later);

        var topics = Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice && topic.ParentId == null).ToList());
        Assert.Equal("Zellbiologie", Assert.Single(topics).Name);
        var mitochondria = Query(db => db.Topics.IgnoreQueryFilters().Single(topic => topic.OwnerId == alice && topic.Name == "Mitochondrien"));
        Assert.Equal(cell.Id, mitochondria.ParentId);
    }

    // Two renames of two siblings to one name at once: one goes through, the other hears the name is taken.
    [Fact]
    public async Task TwoRenamesToOneName_LeaveItOnce()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        await HandleAgainAsync(Analysis(alice, goal));
        var subtopics = Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice && topic.ParentId != null).ToList());
        using var client = ClientFor(alice);

        var answers = await Task.WhenAll(subtopics.Select(async topic =>
        {
            using var response = await client.PatchAsJsonAsync($"/topics/{topic.Id}", new { name = "Organellen" }, Cancel);
            return response.StatusCode;
        }));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], answers.Order());
        Assert.Single(Query(db => db.Topics.IgnoreQueryFilters().Where(topic => topic.OwnerId == alice && topic.Name == "Organellen").ToList()));
    }

    // A save that adds a topic without its TopicSet is a bug in the code, not a state of the data.
    [Fact]
    public void TopicWithoutItsTopicSet_IsRefused()
    {
        using var scope = service.Services.CreateScope();
        var db = DbIn(scope);
        db.Topics.Add(Topic.Create(NewUser(), Guid.NewGuid(), "Zelle", Seed.Now));

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        Assert.Contains("TouchAsync", error.Message, StringComparison.Ordinal);
    }

    // What the consumer does: a scope, the handler, one save.
    private async Task HandleAgainAsync(DocumentAnalyzed analysis)
    {
        using var scope = service.Services.CreateScope();
        await HandlerIn(scope).HandleAsync(analysis, Cancel);
        await DbIn(scope).SaveChangesAsync(Cancel);
    }

    private static DocumentAnalyzedHandler HandlerIn(IServiceScope scope) =>
        ActivatorUtilities.CreateInstance<DocumentAnalyzedHandler>(scope.ServiceProvider);

    private static KnowledgeDbContext DbIn(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();

    private T Query<T>(Func<KnowledgeDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(DbIn(scope));
    }

    private static DocumentAnalyzed Analysis(string user, Guid goal) =>
        new(user, Guid.NewGuid(),
        [
            new("Zelle", null, [Guid.NewGuid()]),
            new("Zellkern", 0, [Guid.NewGuid()]),
            new("Membran", 0, [Guid.NewGuid()]),
        ], goal);

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";
}