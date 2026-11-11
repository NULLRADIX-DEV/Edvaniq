using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.Application;

// The learning records of all users, for the handlers. They run without a current user.
public interface ILearningRecords
{
    Task<bool> ExistsAsync(Guid answerId, CancellationToken cancellationToken);

    // Whether TopicsChanged has named the topic for the owner yet.
    Task<bool> IsTopicKnownAsync(string ownerId, Guid topicId, CancellationToken cancellationToken);

    void Add(LearningRecord record);
}