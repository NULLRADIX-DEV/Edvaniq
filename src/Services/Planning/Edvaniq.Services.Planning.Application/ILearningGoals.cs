using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The learning goals of the current user.
public interface ILearningGoals
{
    // The user's default goal, created on the first call. Calls at the same time all get the same one.
    Task<LearningGoal> DefaultAsync(CancellationToken cancellationToken);

    // A new goal. The first one takes over the default goal's id, of calls at the same time exactly one.
    Task<LearningGoal> CreateAsync(LearningGoalFields fields, CancellationToken cancellationToken);

    // The goals the user created, oldest first. A default goal nobody has taken over is not one of them.
    Task<IReadOnlyList<LearningGoal>> ListAsync(CancellationToken cancellationToken);

    Task<LearningGoal?> FindAsync(Guid id, CancellationToken cancellationToken);

    // Like FindAsync, but also the default goal nobody took over: Content's documents may belong to it.
    Task<LearningGoal?> FindWithDefaultAsync(Guid id, CancellationToken cancellationToken);
}