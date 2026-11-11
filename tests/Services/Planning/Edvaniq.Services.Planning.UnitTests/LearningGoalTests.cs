using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

public sealed class LearningGoalTests
{
    // The key that keeps it to one default goal per user: the same user always gets the same id.
    [Fact]
    public void DefaultId_IsTheSameForTheSameUser()
    {
        Assert.Equal(LearningGoal.DefaultIdFor("alice"), LearningGoal.DefaultIdFor("alice"));
        Assert.Equal(LearningGoal.DefaultIdFor("alice"), LearningGoal.CreateDefault("alice", DateTime.UtcNow).Id);
    }

    // User ids are compared byte for byte, so "Alice" is someone else.
    [Fact]
    public void DefaultId_DiffersPerUser()
    {
        Assert.NotEqual(LearningGoal.DefaultIdFor("alice"), LearningGoal.DefaultIdFor("bob"));
        Assert.NotEqual(LearningGoal.DefaultIdFor("alice"), LearningGoal.DefaultIdFor("Alice"));
    }

    // Pinned, a stored default goal must keep its id across releases.
    [Fact]
    public void DefaultId_DoesNotChange()
    {
        var id = LearningGoal.DefaultIdFor("alice");

        Assert.Equal(8, id.Version);
        Assert.Equal(DefaultIdOfAlice, id);
    }

    [Fact]
    public void DefaultId_NeedsAUser() =>
        Assert.ThrowsAny<ArgumentException>(() => LearningGoal.DefaultIdFor(""));

    private static readonly Guid DefaultIdOfAlice = Guid.Parse("33c7c1a5-662f-8515-8df2-0e5e9cfffe86");
}