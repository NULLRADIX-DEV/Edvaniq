using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.Application;

// The introductions of all users, for the handler. It runs without a current user.
public interface ITopicIntroductions
{
    // Those of the given topics the user has already been introduced to.
    Task<IReadOnlySet<Guid>> IntroducedAsync(string ownerId, IReadOnlyCollection<Guid> topicIds, CancellationToken cancellationToken);

    void Add(TopicIntroduction introduction);
}