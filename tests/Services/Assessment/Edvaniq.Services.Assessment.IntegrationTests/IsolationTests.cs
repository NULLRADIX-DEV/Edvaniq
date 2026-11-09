using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// User A's questions stay invisible to user B, because the owner filter of ServiceDbContext limits every query to the
// current user. Asking questions is #191, so the test asks the DbContext as each user.
public sealed class IsolationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task OtherUser_DoesNotSeeTheQuestions()
    {
        var alice = NewUser("alice");
        var question = await service.AddQuestionAsync(alice);

        Assert.Equal([question.Id], await IdsSeenByAsync(alice));
        Assert.Empty(await IdsSeenByAsync(NewUser("bob")));
    }

    [Fact]
    public async Task OtherUser_DoesNotFindAForeignQuestion()
    {
        var alice = NewUser("alice");
        var question = await service.AddQuestionAsync(alice);

        var found = await AsUserAsync(NewUser("bob"), db => db.Questions.FindAsync([question.Id], TestContext.Current.CancellationToken).AsTask());
        Assert.Null(found);
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_DoesNotSeeTheQuestions()
    {
        var id = Guid.NewGuid().ToString("N");
        var question = await service.AddQuestionAsync($"alice-{id}");

        Assert.Equal([question.Id], await IdsSeenByAsync($"alice-{id}"));
        Assert.Empty(await IdsSeenByAsync($"Alice-{id}"));
    }

    // Every test has its own users, so the tests of this class can share one database.
    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private Task<Guid[]> IdsSeenByAsync(string user) =>
        AsUserAsync(user, db => db.Questions.Select(question => question.Id).ToArrayAsync(TestContext.Current.CancellationToken));

    // The options hold on to their scope, so the scope lives as long as the context.
    private async Task<T> AsUserAsync<T>(string user, Func<AssessmentDbContext, Task<T>> query)
    {
        using var scope = service.Services.CreateScope();
        await using var db = new AssessmentDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AssessmentDbContext>>(), new FixedUser(user));
        return await query(db);
    }

    private sealed class FixedUser(string id) : ICurrentUser
    {
        public string Id => id;
    }
}