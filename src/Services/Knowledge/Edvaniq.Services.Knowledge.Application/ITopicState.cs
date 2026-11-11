namespace Edvaniq.Services.Knowledge.Application;

// The topics of one learning goal as they will be once the open unit of work is saved. Infrastructure implements it.
public interface ITopicState
{
    // Also counts topics added but not saved yet, and leaves out those about to be deleted.
    Task<IReadOnlyList<Guid>> TopicIdsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);
}