using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Knowledge.UnitTests;

// #380: the rank of a topic is its place in the topic list, subtopics right after their topic, the same for the same
// state.
public class TopicFactsTests
{
    private static readonly DateTime First = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ranks_FollowTheTopicList_SubtopicsAfterTheirTopic()
    {
        var (a, b, a1, a2, c) = (Id(1), Id(2), Id(3), Id(4), Id(5));
        TopicInState[] topics =
        [
            new(c, null, CreatedAt: First.AddMinutes(1), Position: 0, Name: "C", Sections: 4),
            new(a2, a, CreatedAt: First, Position: 3, Name: "A2", Sections: 1),
            new(b, null, CreatedAt: First, Position: 1, Name: "B", Sections: 2),
            new(a1, a, CreatedAt: First, Position: 2, Name: "A1"),
            new(a, null, Hidden: true, CreatedAt: First, Position: 0, Name: "A", Sections: 3),
        ];

        var facts = TopicsChangedEvents.Facts(topics);

        Assert.Equal(
            [new TopicFacts(a, 0, 3), new TopicFacts(a1, 1, 0), new TopicFacts(a2, 2, 1), new TopicFacts(b, 3, 2), new TopicFacts(c, 4, 4)],
            facts);
    }

    // Same time and place: by name, then by id, whatever order the state came in.
    [Fact]
    public void Ties_GoByNameThenId()
    {
        TopicInState[] topics = [new(Id(9), null, Name: "X"), new(Id(8), null, Name: "X"), new(Id(7), null, Name: "W")];

        Assert.Equal([Id(7), Id(8), Id(9)], TopicsChangedEvents.Facts(topics).Select(facts => facts.TopicId));
        Assert.Equal(
            TopicsChangedEvents.Facts(topics),
            TopicsChangedEvents.Facts([.. topics.Reverse()]));
    }

    // A count that went below zero in the open save never leaves.
    [Fact]
    public void Sections_AreNeverNegative() =>
        Assert.Equal(0, Assert.Single(TopicsChangedEvents.Facts([new(Id(1), null, Sections: -1)])).Sections);

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");
}