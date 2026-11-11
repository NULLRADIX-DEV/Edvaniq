using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// Questions and attempts of the current user. The owner filter keeps everyone else's out.
public interface IAttempts
{
    // Whether the user has the topic, as TopicsChanged told this service.
    Task<bool> KnowsTopicAsync(Guid topicId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Question>> QuestionsOfAsync(Guid topicId, CancellationToken cancellationToken);

    Task<Question?> FindQuestionAsync(Guid id, CancellationToken cancellationToken);

    Task<Attempt?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Attempt attempt);

    // False if another request answered the attempt first. Nothing of this one is stored then.
    Task<bool> SaveAsync(CancellationToken cancellationToken);
}