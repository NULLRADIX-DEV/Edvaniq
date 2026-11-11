using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Knowledge.Contracts;

// The topics of a learning goal changed (docs/spec/events.md). It carries the whole state, not the change: TopicIds are
// all active topics of the goal, HiddenTopicIds the hidden ones, a topic in neither list is gone. Parents names the
// topic of the upper level for every subtopic in either list (#909), null in messages from before. A receiver only
// takes a state whose ChangedAt (UTC) is newer than the one it took last. Names and order stay with Knowledge.
public sealed record TopicsChanged(
    string UserId,
    Guid LearningGoalId,
    DateTime ChangedAt,
    IReadOnlyList<Guid> TopicIds,
    IReadOnlyList<Guid> HiddenTopicIds,
    IReadOnlyList<TopicParent>? Parents = null) : IIntegrationEvent;

// A subtopic and the topic it belongs to.
public sealed record TopicParent(Guid TopicId, Guid ParentId);