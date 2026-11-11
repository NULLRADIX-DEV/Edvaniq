using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The topics known from TopicsChanged, for its handler and the plan. A consumer has no current user, so every query
// names the owner.
public interface IKnownTopics
{
    Task<TopicsSnapshot?> SnapshotAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);

    // The owner's topics of this learning goal and those with one of the ids, wherever they belong now.
    Task<IReadOnlyList<KnownTopic>> ListAsync(string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken);

    // The ids of the owner's topics of this learning goal that aren't hidden, ascending.
    Task<IReadOnlyList<Guid>> ActiveAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);

    // Those of them that are foundations (#909).
    Task<IReadOnlySet<Guid>> FoundationsAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);

    void Add(TopicsSnapshot snapshot);

    void Add(KnownTopic topic);

    void Remove(KnownTopic topic);
}