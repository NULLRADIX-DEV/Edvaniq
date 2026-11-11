namespace Edvaniq.Services.Content.Application;

// Asks Planning about the learning goals of the current user, a query by rule 6: Planning listens to Content, so Content
// never listens to Planning. Infrastructure implements it with the user's own token, so Planning only ever answers with
// that user's goals.
public interface ILearningGoals
{
    // The default goal. The first call creates it, every later one returns the same. Throws
    // LearningGoalUnavailableException if Planning can't be asked.
    Task<LearningGoalInfo> GetDefaultAsync(CancellationToken cancellationToken);

    // The user's goal with this id, null if they have none. Someone else's goal is no goal of theirs. Throws
    // LearningGoalUnavailableException if Planning can't be asked.
    Task<LearningGoalInfo?> FindAsync(Guid id, CancellationToken cancellationToken);
}

// Level and output language by Planning's names, null when the goal has none. LearnerProfile decides what they mean.
public sealed record LearningGoalInfo(Guid Id, string? Level, string? OutputLanguage);

public sealed class LearningGoalUnavailableException(Exception innerException)
    : Exception("Planning could not be asked for the learning goal.", innerException);