using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The demo of the current user (#411, #414): the documents of DemoMaterial in a learning goal of theirs, analyzed like
// any other document but answered from the demo answers. One demo per user, held by its DemoGoal.
public interface IDemo
{
    // Null if the user has no demo.
    Task<DemoState?> FindAsync(CancellationToken cancellationToken);

    // Pastes the demo material into the goal. A user with a demo gets that one back, also when two requests meet.
    Task<(bool Created, DemoState Demo)> CreateAsync(LearningGoalInfo learningGoal, CancellationToken cancellationToken);

    // Deletes the demo documents with everything made of them, and the demo itself. False if there was none.
    Task<bool> DeleteAsync(CancellationToken cancellationToken);

    // Back to the start before a presentation (#412): the demo documents go as with DeleteAsync, fresh ones come into
    // the same goal. Only the caller's demo documents, nothing else of theirs.
    Task<DemoState> ResetAsync(LearningGoalInfo learningGoal, CancellationToken cancellationToken);
}

// The documents in the order of the material.
public sealed record DemoState(Guid LearningGoalId, IReadOnlyList<Document> Documents);