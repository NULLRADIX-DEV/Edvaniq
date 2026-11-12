namespace Edvaniq.Services.LearningEngine.Domain;

// Why a topic is a weakness (TopicStates): its mastery is below WeakBelow, or only the error rate of its last answers.
public enum WeaknessReason
{
    Mastery,
    RecentErrors,
}

public sealed record WeakTopic(Guid TopicId, int Mastery, WeaknessReason Reason);

public sealed record SecureTopic(Guid TopicId, int Mastery);

public sealed record WeakAndSecureTopics(IReadOnlyList<WeakTopic> Weaknesses, IReadOnlyList<SecureTopic> Secure);

// The user's weaknesses and secure topics (#453), from the standings TopicStates worked out, so the lists can't
// disagree with the states. The worst weakness first: the lowest mastery is the most severe, the same "weakest topic" the
// planner and A62 mean, so there is one measure and not two. A weakness only by its error rate has the higher mastery,
// so it comes after those below WeakBelow. Secure topics the other way round, the highest first. Ties go by topic id.
// New topics and those building are in neither list.
public static class WeakAndSecure
{
    public static WeakAndSecureTopics Of(IEnumerable<(Guid TopicId, TopicStanding Standing)> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        var all = topics.ToList();
        return new WeakAndSecureTopics(
            [
                .. all.Where(topic => topic.Standing.State == TopicState.Weakness)
                    .OrderBy(topic => topic.Standing.Mastery)
                    .ThenBy(topic => topic.TopicId)
                    .Select(topic => new WeakTopic(
                        topic.TopicId,
                        topic.Standing.Mastery,
                        topic.Standing.Mastery < TopicStates.WeakBelow ? WeaknessReason.Mastery : WeaknessReason.RecentErrors)),
            ],
            [
                .. all.Where(topic => topic.Standing.State == TopicState.Secure)
                    .OrderByDescending(topic => topic.Standing.Mastery)
                    .ThenBy(topic => topic.TopicId)
                    .Select(topic => new SecureTopic(topic.TopicId, topic.Standing.Mastery)),
            ]);
    }
}