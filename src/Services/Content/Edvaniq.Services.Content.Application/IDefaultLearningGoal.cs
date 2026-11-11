namespace Edvaniq.Services.Content.Application;

// Asks Planning for the default learning goal of the current user, a query by rule 6: Planning listens to Content, so
// Content never listens to Planning. The first call creates the goal, every later one returns the same. Infrastructure
// implements it with the user's own token.
public interface IDefaultLearningGoal
{
    // Throws LearningGoalUnavailableException if Planning can't be asked.
    Task<Guid> IdAsync(CancellationToken cancellationToken);
}

public sealed class LearningGoalUnavailableException(Exception innerException)
    : Exception("Planning could not be asked for the default learning goal.", innerException);