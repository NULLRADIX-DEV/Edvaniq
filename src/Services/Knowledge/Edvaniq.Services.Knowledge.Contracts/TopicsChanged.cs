using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Knowledge.Contracts;

// The topics of a learning goal changed (docs/spec/events.md). It carries the whole state, not the change: TopicIds are
// all active topics of the goal, HiddenTopicIds the hidden ones, a topic in neither list is gone. Parents names the
// topic of the upper level for every subtopic in either list (#909), null in messages from before. Topics gives every
// topic of either list its place in the material and its size (#380), null in messages from before. The order of the
// two id lists means nothing. A receiver only takes a state whose ChangedAt (UTC) is newer than the one it took last.
// Names stay with Knowledge.
public sealed record TopicsChanged(
    string UserId,
    Guid LearningGoalId,
    DateTime ChangedAt,
    IReadOnlyList<Guid> TopicIds,
    IReadOnlyList<Guid> HiddenTopicIds,
    IReadOnlyList<TopicParent>? Parents = null,
    IReadOnlyList<TopicFacts>? Topics = null) : IUserEvent;

// A subtopic and the topic it belongs to.
public sealed record TopicParent(Guid TopicId, Guid ParentId);

// Rank: the place in the material, from 0, as the topic list shows it: the topics of the upper level, each followed by
// its subtopics. Sections: how many sections of the material belong to the topic, 0 for one the user added.
public sealed record TopicFacts(Guid TopicId, int Rank, int Sections);