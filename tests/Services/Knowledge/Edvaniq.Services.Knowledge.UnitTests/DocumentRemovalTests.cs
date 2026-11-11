using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.UnitTests;

// A topic goes with a deleted document only if nothing else holds it: no section of another document, no subtopic that
// stays. Topics the user added stay.
public class DocumentRemovalTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Goal = Guid.NewGuid();

    private static readonly Guid Deleted = Guid.NewGuid();

    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void TopicOnlyOfTheDocument_Goes_WithItsSubtopics()
    {
        var topic = Topic.Create("alice", Goal, "Photosynthese", Now, Deleted);
        var light = topic.AddSubtopic("Lichtreaktion", Now, Deleted);
        var calvin = topic.AddSubtopic("Calvin-Zyklus", Now, Deleted, 1);

        var removed = Remove([topic, light, calvin], withSections: [light.Id, calvin.Id], withOtherSections: []);

        Assert.Equal(new HashSet<Guid> { topic.Id, light.Id, calvin.Id }, removed);
    }

    [Fact]
    public void TopicWithSectionsOfAnotherDocument_Stays()
    {
        var shared = Topic.Create("alice", Goal, "Zellatmung", Now, Deleted);

        Assert.Empty(Remove([shared], withSections: [shared.Id], withOtherSections: [shared.Id]));
    }

    [Fact]
    public void Parent_StaysWithASubtopicThatStays_TheOtherSubtopicGoes()
    {
        var topic = Topic.Create("alice", Goal, "Photosynthese", Now, Deleted);
        var light = topic.AddSubtopic("Lichtreaktion", Now, Deleted);
        var calvin = topic.AddSubtopic("Calvin-Zyklus", Now, Other);

        var removed = Remove([topic, light, calvin], withSections: [light.Id], withOtherSections: [calvin.Id]);

        Assert.Equal(new HashSet<Guid> { light.Id }, removed);
    }

    [Fact]
    public void TopicOfAnotherDocumentOrOfTheUser_StaysEvenWithoutSections()
    {
        var own = Topic.Create("alice", Goal, "Eigenes Thema", Now);
        var subtopic = own.AddSubtopic("Aus dem Dokument", Now, Deleted);
        var other = Topic.Create("alice", Goal, "Anderes Dokument", Now, Other);

        var removed = Remove([own, subtopic, other], withSections: [subtopic.Id], withOtherSections: []);

        Assert.Equal(new HashSet<Guid> { subtopic.Id }, removed);
    }

    private static IReadOnlySet<Guid> Remove(IReadOnlyList<Topic> topics, HashSet<Guid> withSections, HashSet<Guid> withOtherSections) =>
        DocumentRemoval.TopicsToRemove(Deleted, topics, withSections, withOtherSections);
}