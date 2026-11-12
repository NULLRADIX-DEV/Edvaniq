using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// User A's learning records stay invisible to user B, because the owner filter of ServiceDbContext limits every query
// to the current user. Every endpoint with a user's token is listed here (IsolationCheck, #271), the records also through
// the DbContext as each user.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    internal static IsolationCheck Check() => new IsolationCheck()
        .Exempt("GET", "/me", "Only the caller's own id from the token.")
        .Item("GET", "/mastery/{topicId:guid}")
        .List("GET", "/mastery")
        .Exempt("GET", "/mastery/decayed", "Needs a goal, per user: DecayedMasteryTests.OnlyTheAskedGoal_OfTheUserFromTheToken.")
        .Exempt("GET", "/mastery/weak-and-secure", "Per user and goal: WeakAndSecureTests.EachUser_SeesOnlyTheirOwnTopics.");

    [Fact]
    public void EveryEndpoint_IsInTheCheck() => Check().AssertCovers(service.Services);

    [Fact]
    public Task OtherUser_CanNeitherSeeNorChangeTheMastery() => Check().RunAsync(service, SeedAsync);

    [Fact]
    public async Task OtherUser_DoesNotSeeTheRecords()
    {
        var alice = NewUser("alice");
        var record = await service.AddRecordAsync(alice);

        Assert.Equal([record.AnswerId], await AnswerIdsSeenByAsync(alice));
        Assert.Empty(await AnswerIdsSeenByAsync(NewUser("bob")));
    }

    [Fact]
    public async Task OtherUser_DoesNotFindAForeignRecord()
    {
        var alice = NewUser("alice");
        var record = await service.AddRecordAsync(alice);

        var found = await AsUserAsync(NewUser("bob"), db => db.LearningRecords.FindAsync([record.AnswerId], TestContext.Current.CancellationToken).AsTask());
        Assert.Null(found);
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheRecords()
    {
        var id = Guid.NewGuid().ToString("N");
        var record = await service.AddRecordAsync($"alice-{id}");

        Assert.Equal([record.AnswerId], await AnswerIdsSeenByAsync($"alice-{id}"));
        Assert.Empty(await AnswerIdsSeenByAsync($"Alice-{id}"));
    }

    // A topic of A with two answers, so it has a mastery to read.
    private async Task<OwnedResource> SeedAsync(string owner)
    {
        var topic = Guid.NewGuid();
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.Add(KnownTopic.Create(owner, topic, Guid.NewGuid(), hidden: false));
        db.LearningRecords.AddRange(Enumerable.Range(0, 2).Select(index => LearningRecord.Create(
            Guid.NewGuid(), owner, topic, Guid.NewGuid(), null, true, 2, 4200, null, Mode.Practice, Seed.Now.AddMinutes(index))));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new OwnedResource(topic, $"/mastery/{topic}", topic.ToString());
    }

    // Every test has its own users, so the tests of this class can share one database.
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private Task<Guid[]> AnswerIdsSeenByAsync(string user) =>
        AsUserAsync(user, db => db.LearningRecords.Select(record => record.AnswerId).ToArrayAsync(TestContext.Current.CancellationToken));

    // The options hold on to their scope, so the scope lives as long as the context.
    private async Task<T> AsUserAsync<T>(string user, Func<LearningEngineDbContext, Task<T>> query)
    {
        using var scope = service.Services.CreateScope();
        await using var db = new LearningEngineDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<LearningEngineDbContext>>(), new FixedUser(user));
        return await query(db);
    }

    private sealed class FixedUser(string id) : ICurrentUser
    {
        public string Id => id;
    }
}