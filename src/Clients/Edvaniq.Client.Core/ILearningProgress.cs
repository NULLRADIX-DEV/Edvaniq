namespace Edvaniq.Client.Core;

// The mastery of the user's topics from LearningEngine, through the web host.
public interface ILearningProgress
{
    Task<CallOutcome<IReadOnlyList<TopicProgress>>> ListAsync(CancellationToken cancellationToken = default);
}

// State is LearningEngine's, never worked out here, null for one this client doesn't know yet. LastAnswerId is the
// newest answer LearningEngine has for the topic: once it is the user's last one, mastery and plan contain it.
public sealed record TopicProgress(Guid TopicId, int Mastery, TopicState? State, Guid? LastAnswerId);

public enum TopicState
{
    New,
    Weakness,
    Building,
    Secure,
}