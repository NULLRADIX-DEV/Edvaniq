namespace Edvaniq.Services.Knowledge.Application;

// The topics of one learning goal as they will be once the open unit of work is saved. Infrastructure implements it.
public interface ITopicState
{
    // Also counts topics added but not saved yet, and leaves out those about to be deleted.
    Task<IReadOnlyList<TopicInState>> TopicsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);

    // Marks the learning goal's topics as changed by the open unit of work (TopicSet), once per save. Every save that
    // changes topics has to, KnowledgeDbContext refuses one that doesn't.
    Task TouchAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);
}

// ParentId is the topic of the upper level, null for a topic of the upper level itself. Hidden is the topic's own state
// (Topic.Hidden), not the one it gets from its parent. CreatedAt, Position and Name give the order of the material as the
// topic list shows it, Sections counts the sections of the topic, also those about to be added or deleted (#380).
public sealed record TopicInState(
    Guid Id, Guid? ParentId, bool Hidden = false, DateTime CreatedAt = default, int Position = 0, string Name = "", int Sections = 0);