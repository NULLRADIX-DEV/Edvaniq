using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.Application;

// The mastery and state of the current user's topics. The owner filter keeps other users' topics and records out.
public interface IMasteries
{
    // Null if the user doesn't know the topic (yet): someone else's and an unknown one look the same.
    Task<TopicStanding?> FindAsync(Guid topicId, CancellationToken cancellationToken);

    // Every topic the user knows, or those of one learning goal, practised or not, ordered by id. Someone else's
    // learning goal and one without topics both give an empty list.
    Task<IReadOnlyList<TopicMastery>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken);
}

public sealed record TopicMastery(Guid TopicId, int Mastery, TopicState State);