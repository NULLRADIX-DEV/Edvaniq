using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// What TopicsChanged clears up when a topic leaves its learning goal (#412). Runs in a consumer without a current user,
// so every query names the owner.
public interface ITopicCleanup
{
    // Marks the owner's done blocks of the goal on these topics for removal with the next save, every day. Returns how
    // many.
    Task<int> RemoveDoneBlocksAsync(string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken);

    // The owner's running session, tracked, so the next save stores what changed on it.
    Task<Session?> ActiveSessionAsync(string ownerId, CancellationToken cancellationToken);

    // Every session of the owner on the block, running or ended.
    Task<IReadOnlyList<Session>> OnBlockAsync(string ownerId, Guid blockId, CancellationToken cancellationToken);
}