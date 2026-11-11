using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The topics known from TopicsChanged, for its handler. A consumer has no current user, so every query names the owner.
public interface IKnownTopics
{
    Task<TopicsSnapshot?> SnapshotAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);

    Task<bool> IsKnownAsync(string ownerId, Guid topicId, CancellationToken cancellationToken);

    // The owner's topics of this learning goal and those with one of the ids, wherever they belong now.
    Task<IReadOnlyList<KnownTopic>> ListAsync(string ownerId, Guid learningGoalId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken);

    void Add(TopicsSnapshot snapshot);

    void Add(KnownTopic topic);

    void Remove(KnownTopic topic);
}