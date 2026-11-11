namespace Edvaniq.Services.Planning.Application;

// The mastery of the current user's topics, asked from LearningEngine (rule 6: Planning never listens to it). Infrastructure
// implements it with the caller's own token.
public interface IMasterySource
{
    // Mastery per topic id of the learning goal, null when LearningEngine can't be asked.
    Task<IReadOnlyDictionary<Guid, int>?> OfLearningGoalAsync(Guid learningGoalId, CancellationToken cancellationToken);
}