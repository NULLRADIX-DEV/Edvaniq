using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.UnitTests;

// A topic gets an id of its own that a rename leaves alone, and topics have two levels.
public class TopicTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Goal = Guid.NewGuid();

    [Fact]
    public void NewTopics_GetIdsOfTheirOwn()
    {
        var first = Topic.Create("alice", Goal, "Zelle", Now);
        var second = Topic.Create("alice", Goal, "Zelle", Now);

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Rename_KeepsTheId()
    {
        var topic = Topic.Create("alice", Goal, "Zelle", Now);
        var id = topic.Id;

        topic.Rename("  Zellbiologie ");

        Assert.Equal((id, "Zellbiologie"), (topic.Id, topic.Name));
    }

    [Fact]
    public void Subtopic_BelongsToItsTopic_AndToTheSameUserAndLearningGoal()
    {
        var topic = Topic.Create("alice", Goal, " Zelle ", Now);

        var subtopic = topic.AddSubtopic(" Zellkern ", Now);

        Assert.False(topic.IsSubtopic);
        Assert.True(subtopic.IsSubtopic);
        Assert.Equal((topic.Id, "alice", Goal), (subtopic.ParentId, subtopic.OwnerId, subtopic.LearningGoalId));
        Assert.Equal(("Zelle", "Zellkern"), (topic.Name, subtopic.Name));
        Assert.NotEqual(topic.Id, subtopic.Id);
    }

    [Fact]
    public void Subtopic_HasNoSubtopics() =>
        Assert.Throws<InvalidOperationException>(() =>
            Topic.Create("alice", Goal, "Zelle", Now).AddSubtopic("Zellkern", Now).AddSubtopic("Kernhülle", Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void EmptyName_IsRejected(string? name) =>
        Assert.Equal("Der Name ist leer. Gib dem Thema einen Namen.", Topic.CheckName(name));

    [Fact]
    public void NameAtTheLimit_IsAccepted_EvenWithSpaceAround() =>
        Assert.Null(Topic.CheckName($"  {new string('a', Topic.NameMaxLength)}  "));

    [Fact]
    public void NameOverTheLimit_IsRejected() =>
        Assert.Equal(
            "Der Name ist zu lang. Erlaubt sind höchstens 80 Zeichen.",
            Topic.CheckName(new string('a', Topic.NameMaxLength + 1)));
}