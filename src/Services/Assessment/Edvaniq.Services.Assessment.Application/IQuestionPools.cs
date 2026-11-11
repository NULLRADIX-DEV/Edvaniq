namespace Edvaniq.Services.Assessment.Application;

// How many usable questions the current user has per topic. Usable means stored with the topic's id: questions taken
// over before topic ids came are never asked, so they don't count. The owner filter keeps other users' out.
public interface IQuestionPools
{
    // Null if the user doesn't know the topic: someone else's and an unknown one look the same.
    Task<TopicPool?> FindAsync(Guid topicId, CancellationToken cancellationToken);

    // Every topic the user knows, or those of one learning goal, ordered by id, those without questions with 0.
    Task<IReadOnlyList<TopicPool>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken);
}

public sealed record TopicPool(Guid TopicId, int Questions);