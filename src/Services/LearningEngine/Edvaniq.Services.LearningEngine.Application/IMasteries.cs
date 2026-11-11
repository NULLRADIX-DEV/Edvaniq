using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.Application;

// The mastery and state of the current user's topics. The owner filter keeps other users' topics and records out.
public interface IMasteries
{
    // Null if the user doesn't know the topic (yet): someone else's and an unknown one look the same.
    Task<TopicMastery?> FindAsync(Guid topicId, CancellationToken cancellationToken);

    // Every topic the user knows, or those of one learning goal, practised or not, ordered by id. Someone else's
    // learning goal and one without topics both give an empty list.
    Task<IReadOnlyList<TopicMastery>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken);

    // The topics of one learning goal that were secure at their last answer and have decayed since
    // (TopicStates.Decayed), the lowest mastery first.
    Task<IReadOnlyList<DecayedTopicMastery>> DecayedAsync(Guid learningGoalId, CancellationToken cancellationToken);
}

// LastAnswerId: the answer of the newest learning record of the topic, counted or not, null without any.
public sealed record TopicMastery(Guid TopicId, int Mastery, TopicState State, Guid? LastAnswerId = null);

public sealed record DecayedTopicMastery(Guid TopicId, DecayedTopic Standing);