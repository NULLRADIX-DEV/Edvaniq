namespace Edvaniq.Services.Planning.Application;

// Which of the current user's topics have a ready question pool, asked from Assessment (#374). Whether a pool is ready
// is Assessment's rule, Planning only takes its answer. Infrastructure implements it with the caller's own token.
public interface IQuestionPoolSource
{
    // The ids of the learning goal's topics whose pool is ready, null when Assessment can't be asked.
    Task<IReadOnlySet<Guid>?> ReadyTopicsAsync(Guid learningGoalId, CancellationToken cancellationToken);
}