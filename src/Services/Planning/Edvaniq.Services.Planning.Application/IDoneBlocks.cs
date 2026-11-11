using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The done blocks of the current user. Finishing a session adds one (#395), until then only tests do.
public interface IDoneBlocks
{
    // The blocks of the goal that were planned for the study day and are done.
    Task<IReadOnlyList<DoneBlock>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken);

    // Adding a block that is done already changes nothing.
    Task AddAsync(DoneBlock block, CancellationToken cancellationToken);
}