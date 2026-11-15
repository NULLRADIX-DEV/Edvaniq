using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Where topics stood at two moments and which answers LearningEngine has (#403), asked from LearningEngine like
// IMasterySource, with the caller's own token.
public interface IMasteryChanges
{
    // Null when LearningEngine can't be asked.
    Task<MasteryChanges?> BetweenAsync(
        IReadOnlyCollection<Guid> topicIds, SessionWindow window, IReadOnlyCollection<Guid> answerIds, CancellationToken cancellationToken);
}