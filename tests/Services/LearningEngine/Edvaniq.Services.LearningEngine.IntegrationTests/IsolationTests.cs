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

        Assert.Equal([record.AnswerId], AnswerIdsSeenBy(alice));
        Assert.Empty(AnswerIdsSeenBy(NewUser("bob")));
    }

    [Fact]
    public async Task OtherUser_DoesNotFindAForeignRecord()
    {
        var alice = NewUser("alice");
        var record = await service.AddRecordAsync(alice);

        using var asBob = ContextFor(NewUser("bob"));
        Assert.Null(await asBob.LearningRecords.FindAsync([record.AnswerId], TestContext.Current.CancellationToken));
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheRecords()
    {
        var id = Guid.NewGuid().ToString("N");
        var record = await service.AddRecordAsync($"alice-{id}");

        Assert.Equal([record.AnswerId], AnswerIdsSeenBy($"alice-{id}"));
        Assert.Empty(AnswerIdsSeenBy($"Alice-{id}"));
    }

    // Every test has its own users, so the tests of this class can share one database.
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private Guid[] AnswerIdsSeenBy(string user)
    {
        using var db = ContextFor(user);
        return [.. db.LearningRecords.Select(record => record.AnswerId)];
    }

    private LearningEngineDbContext ContextFor(string user)
    {
        using var scope = service.Services.CreateScope();
        return new(scope.ServiceProvider.GetRequiredService<DbContextOptions<LearningEngineDbContext>>(), new FixedUser(user));
    }

    private sealed class FixedUser(string id) : ICurrentUser
    {
        public string Id => id;
    }
}