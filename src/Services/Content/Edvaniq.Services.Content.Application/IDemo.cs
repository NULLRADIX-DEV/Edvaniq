using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The demo material of the current user (#411): the documents of DemoMaterial in a learning goal of theirs, analyzed like
// any other document but answered from the demo answers. One demo per user.
public interface IDemo
{
    // The user's demo documents in the order of the material, empty if they have no demo.
    Task<IReadOnlyList<Document>> DocumentsAsync(CancellationToken cancellationToken);

    // Pastes the demo material into the goal. Only for a user without demo documents.
    Task<IReadOnlyList<Document>> CreateAsync(LearningGoalInfo learningGoal, CancellationToken cancellationToken);
}