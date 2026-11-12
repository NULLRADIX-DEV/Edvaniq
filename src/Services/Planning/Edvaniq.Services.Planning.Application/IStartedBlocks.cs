namespace Edvaniq.Services.Planning.Application;

// The blocks of the current user that a session was started on, running or ended (#407).
public interface IStartedBlocks
{
    // The ids of the goal's blocks planned for the study day that have a session.
    Task<IReadOnlySet<Guid>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken);
}