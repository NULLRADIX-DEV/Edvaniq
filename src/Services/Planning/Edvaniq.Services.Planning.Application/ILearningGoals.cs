using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The learning goals of the current user.
public interface ILearningGoals
{
    // The user's default goal, created on the first call. Calls at the same time all get the same one.
    Task<LearningGoal> DefaultAsync(CancellationToken cancellationToken);
}