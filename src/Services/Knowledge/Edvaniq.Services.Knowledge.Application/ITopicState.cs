namespace Edvaniq.Services.Knowledge.Application;

// The topics of one learning goal as they will be once the open unit of work is saved. Infrastructure implements it.
public interface ITopicState
{
    // Also counts topics added but not saved yet, and leaves out those about to be deleted.
    Task<IReadOnlyList<TopicInState>> TopicsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);
}

// ParentId is the topic of the upper level, null for a topic of the upper level itself. Hidden is the topic's own state
// (Topic.Hidden), not the one it gets from its parent.
public sealed record TopicInState(Guid Id, Guid? ParentId, bool Hidden = false);