namespace Edvaniq.Services.Content.Application;

// Level and output language of a learning goal, asked from Planning before every AI step of a document (rule 6: Content
// asks), so a change of the goal counts at the next analysis. Content keeps no copy of them. Infrastructure asks with
// the worker's own token.
public interface IGoalProfiles
{
    // Null if Planning has no such goal (any more). Throws LearningGoalUnavailableException if Planning can't be asked.
    Task<LearnerProfile?> OfAsync(Guid learningGoalId, CancellationToken cancellationToken);
}