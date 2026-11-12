using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The done blocks of the current user. Finishing a session adds one (#395).
public interface IDoneBlocks
{
    // The blocks of the goal that were planned for the study day and are done.
    Task<IReadOnlyList<DoneBlock>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken);

    // Stored with the next save of the session that finished it, in the same transaction.
    void Add(DoneBlock block);
}