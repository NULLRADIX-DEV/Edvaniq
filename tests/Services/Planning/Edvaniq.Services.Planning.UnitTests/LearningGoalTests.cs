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

    private static readonly DateTime Created = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    // #516: everything but the kind changes, and the goal says when.
    [Fact]
    public void Update_ChangesTheFields_KeepsKindAndCreatedAt()
    {
        var goal = LearningGoal.Create(Guid.NewGuid(), "alice", Exam("Mathe", new DateOnly(2026, 12, 1)), Created);
        var at = new DateTime(2026, 11, 12, 9, 30, 0, DateTimeKind.Utc).AddTicks(7);

        goal.Update(
            new LearningGoalFields(LearningGoalKind.ExamPreparation, "Mathe neu", ExamType.Klausur, new DateOnly(2026, 12, 15), LearningLevel.UpperSecondary, "en", "eine 1"),
            at);

        Assert.Equal(
            (LearningGoalKind.ExamPreparation, "Mathe neu", ExamType.Klausur, new DateOnly(2026, 12, 15), LearningLevel.UpperSecondary, "en", "eine 1"),
            (goal.Kind, goal.Name, goal.ExamType, goal.ExamDate, goal.Level, goal.OutputLanguage, goal.Goal));
        Assert.Equal(Created, goal.CreatedAt);
        Assert.Equal(at.AddTicks(-7), goal.UpdatedAt);
    }

    [Fact]
    public void UpdatedAt_IsNullUntilTheFirstChange() =>
        Assert.Null(LearningGoal.Create(Guid.NewGuid(), "alice", Exam("Mathe", null), Created).UpdatedAt);

    [Fact]
    public void Update_WithAnotherKind_Throws()
    {
        var goal = LearningGoal.Create(Guid.NewGuid(), "alice", Exam("Mathe", null), Created);

        Assert.Throws<ArgumentException>(() =>
            goal.Update(new LearningGoalFields(LearningGoalKind.FreeLearning, "Mathe", null, null, null, "de", null), Created));
        Assert.Equal(LearningGoalKind.ExamPreparation, goal.Kind);
        Assert.Null(goal.UpdatedAt);
    }

    [Fact]
    public void Update_OfADefaultGoalNobodyTookOver_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            LearningGoal.CreateDefault("alice", Created).Update(Exam("Mathe", null), Created));

    private static LearningGoalFields Exam(string name, DateOnly? date) =>
        new(LearningGoalKind.ExamPreparation, name, ExamType.Schulaufgabe, date, null, "de", null);

    private static readonly Guid DefaultIdOfAlice = Guid.Parse("33c7c1a5-662f-8515-8df2-0e5e9cfffe86");
}