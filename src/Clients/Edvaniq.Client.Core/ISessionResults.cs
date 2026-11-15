namespace Edvaniq.Client.Core;

// What an ended session brought, from Planning through the web host (#403, #406). The counts are right from the first
// answer, the mastery only comes once LearningEngine has every answer of the session: until then Complete is false and
// Topics null, never an old value.
public interface ISessionResults
{
    // One look. NotReady while the session runs, NotFound for one that isn't the user's.
    Task<CallOutcome<SessionReport>> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    // Asks every Interval until the report is complete or its topic left the goal, up to Limit of SessionResultPolling.
    // Each report on the way goes to progress, so the counts show at once. Ends with the last answer: Ok with Complete
    // false means LearningEngine is behind for longer than the limit, the caller offers to check again.
    Task<CallOutcome<SessionReport>> WaitForCompleteAsync(
        Guid sessionId, IProgress<SessionReport>? progress = null, CancellationToken cancellationToken = default);
}

// Completed: finished, false when broken off. Answered, Correct and Wrong count the answers on the block up to this
// session, a shown solution as wrong. Complete and Topics as above, MasteryAvailable false when Planning couldn't ask
// LearningEngine. TopicRemoved: the topic left the goal since, no mastery will come. Next is null for a bonus review.
public sealed record SessionReport(
    Guid SessionId,
    bool Completed,
    int Answered,
    int Correct,
    int Wrong,
    bool Complete,
    bool MasteryAvailable,
    IReadOnlyList<MasteryChange>? Topics,
    DailyGoalState? DailyGoal,
    NextBlock? Next,
    bool TopicRemoved)
{
    // Nothing more will change it by waiting.
    public bool IsSettled => Complete || TopicRemoved;
}

// Mastery in whole percent. Before null when LearningEngine can't tell the start any more. State null for one this
// client doesn't know yet.
public sealed record MasteryChange(Guid TopicId, MasteryStanding? Before, MasteryStanding? After);

public sealed record MasteryStanding(int Mastery, string? State);

public sealed record NextBlock(Guid BlockId, Guid TopicId, BlockKind? Kind, int Minutes);

public enum DailyGoalState
{
    None,
    Open,
    Reached,
}

// How often WaitForCompleteAsync asks and when it stops by itself. LearningEngine has the answers of a session within
// seconds. 45 s of waiting means it is behind, much less than the 5 minutes after which the server warns of missing
// answers, so the user isn't kept waiting and the page offers to check again.
public sealed record SessionResultPolling(TimeSpan Interval, TimeSpan Limit)
{
    public static readonly SessionResultPolling Default = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(45));
}