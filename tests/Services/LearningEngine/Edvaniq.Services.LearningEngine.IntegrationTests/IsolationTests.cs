using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.LearningEngine.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// User A's learning records stay invisible to user B, because the owner filter of ServiceDbContext limits every query
// to the current user. LearningEngine has no endpoint for its records yet, so the test asks the DbContext as each user.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
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