using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// User A's data stays invisible to user B, because the owner filter of ServiceDbContext limits every query to the user
// from the token. Every endpoint with a user's token is listed here (IsolationCheck), against a real database.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    internal static IsolationCheck Check() => new IsolationCheck()
        .Exempt("GET", "/me", "Only the caller's own id from the token.")
        .List("GET", "/topics")
        .Item("GET", "/topics/{id:guid}")
        .Item("PATCH", "/topics/{id:guid}", new { name = "Von Bob" })
        .Item("PUT", "/topics/{id:guid}/hidden")
        .Item("DELETE", "/topics/{id:guid}/hidden")
        .Exempt("POST", "/topics", "Adds a topic of the caller. A foreign parent: OtherUser_CannotAddASubtopicToAForeignTopic.");

    [Fact]
    public void EveryEndpoint_IsInTheCheck() => Check().AssertCovers(service.Services);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorChangeTheTopics() => Check().RunAsync(service, owner => SeedAsync(service, owner));

    // The check itself: an endpoint the list forgets fails it.
    [Fact]
    public void Check_FailsForAnEndpointItDoesNotList()
    {
        var incomplete = new IsolationCheck()
            .Exempt("GET", "/me", "Only the caller's own id from the token.")
            .List("GET", "/topics")
            .Item("GET", "/topics/{id:guid}");

        var error = Assert.Throws<IsolationCheckException>(() => incomplete.AssertCovers(service.Services));
        Assert.Contains("PATCH /topics/{id:guid}", error.Message);
    }

    // A's topic as parent looks to B exactly like one that does not exist, and A's topics stay as they were.
    [Fact]
    public async Task OtherUser_CannotAddASubtopicToAForeignTopic()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var bob = ClientFor($"bob-{Guid.NewGuid():N}");
        var topic = await service.AddTopicAsync(alice, "Zellbiologie");

        var foreign = await AddSubtopicAsync(bob, topic.Id);
        var missing = await AddSubtopicAsync(bob, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        Assert.Equal(missing, foreign);
        using var aliceClient = ClientFor(alice);
        var tree = await aliceClient.GetStringAsync("/topics", TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Von Bob", tree);
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheItems()
    {
        var id = Guid.NewGuid().ToString("N");
        using var upper = ClientFor($"Alice-{id}");

        var topic = await service.AddTopicAsync($"alice-{id}", "Zellbiologie");

        using var response = await upper.GetAsync($"/topics/{topic.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal static async Task<OwnedResource> SeedAsync(ServiceFactory service, string owner)
    {
        var topic = await service.AddTopicAsync(owner, $"Zellbiologie {Guid.NewGuid():N}");
        return new OwnedResource(topic.Id, $"/topics/{topic.Id}", topic.Name);
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    // The problem without the trace, which differs on every request.
    private static async Task<(HttpStatusCode Status, string Problem)> AddSubtopicAsync(HttpClient client, Guid parentId)
    {
        using var response = await client.PostAsJsonAsync("/topics", new { name = "Von Bob", parentId }, TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        problem!.Remove("traceId");
        return (response.StatusCode, problem.ToJsonString());
    }
}

// The proof that the check finds a leak: the same service, but reading and renaming a topic past the owner filter.
public sealed class IsolationLeakTests(LeakyServiceFactory service) : IClassFixture<LeakyServiceFactory>
{
    [Fact]
    public async Task Check_FailsForATopicReadPastTheOwnerFilter()
    {
        var error = await Assert.ThrowsAsync<IsolationCheckException>(
            () => IsolationTests.Check().RunAsync(service, owner => IsolationTests.SeedAsync(service, owner)));

        Assert.Contains("/topics/{id:guid}", error.Message);
    }
}

public sealed class LeakyServiceFactory(MySqlTestServer mysql) : ServiceFactory(mysql)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddScoped<ITopics, LeakyTopics>());
    }

    private sealed class LeakyTopics(KnowledgeDbContext db, ITopicState state) : ITopics
    {
        public async Task<IReadOnlyList<Topic>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken) =>
            await db.Topics.AsNoTracking().ToListAsync(cancellationToken);

        public Task<Topic?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            AllTopics().AsNoTracking().FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken);

        public async Task<TopicResult> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
        {
            var topic = await AllTopics().FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken);
            if (topic is null)
            {
                return TopicResult.Fail(TopicError.NotFound);
            }

            topic.Rename(name);
            await state.TouchAsync(topic.OwnerId, topic.LearningGoalId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return TopicResult.Ok(topic);
        }

        public Task<TopicResult> AddAsync(Guid learningGoalId, Guid? parentId, string name, CancellationToken cancellationToken) =>
            Task.FromResult(TopicResult.Fail(TopicError.NotFound));

        public Task<TopicResult> SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken) =>
            Task.FromResult(TopicResult.Fail(TopicError.NotFound));

        private IQueryable<Topic> AllTopics() => db.Topics.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]);
    }
}