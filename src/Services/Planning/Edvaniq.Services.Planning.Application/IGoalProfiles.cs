using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Any user's goal by its id, for Content's worker only (GET /internal/learning-goals/{id}/profile). It has no user to
// filter by, and it only knows a goal id from a document whose goal Content checked for its owner when it came in.
public interface IGoalProfiles
{
    Task<LearningGoal?> FindAsync(Guid id, CancellationToken cancellationToken);
}