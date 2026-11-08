using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// The topics of the current user. Infrastructure implements it, the owner filter keeps other users' topics out.
public interface ITopics
{
    // Both levels, oldest first. Without a learning goal all of the user's topics.
    Task<IReadOnlyList<Topic>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken);

    Task<Topic?> FindAsync(Guid id, CancellationToken cancellationToken);

    // Null if the user has no such topic. The name must have passed Topic.CheckName.
    Task<Topic?> RenameAsync(Guid id, string name, CancellationToken cancellationToken);
}