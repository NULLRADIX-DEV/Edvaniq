namespace Edvaniq.Client.Core;

// The learning sessions of the user, from Planning through the web host (#376, #393, #395, #396).
public interface ISessions
{
    // Starts the session of a block of today's plan, or brings back the one the user already has (Existing).
    // Rejected with Planning's sentence when the block is done, NotFound with it when the block left the plan.
    Task<CallOutcome<SessionInfo>> StartAsync(Guid blockId, CancellationToken cancellationToken = default);

    // A bonus review after the plan (#408), or the one the user already has. Rejected with Planning's sentence when no
    // topic fits, Failed when Planning can't put one together right now.
    Task<CallOutcome<SessionInfo>> StartBonusAsync(CancellationToken cancellationToken = default);

    // Only forward, a step at or before the current one changes nothing. Rejected with Planning's sentence once the
    // session has ended, a topic that left the goal ends it too.
    Task<CallOutcome<SessionInfo>> AdvanceAsync(Guid sessionId, int step, CancellationToken cancellationToken = default);

    // Both give the session as it is when it has ended already.
    Task<CallOutcome<SessionInfo>> CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<CallOutcome<SessionInfo>> AbortAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

// Existing: the user had this session already, a start brought it back. Steps in their fixed order (#377), CurrentStep
// the index the user is at. Outcome null for one this client doesn't know yet. Bonus: a bonus review, its block is no
// block of the plan.
public sealed record SessionInfo(
    Guid Id,
    Guid BlockId,
    Guid TopicId,
    bool Existing,
    IReadOnlyList<SessionStep> Steps,
    int CurrentStep,
    SessionOutcome? Outcome,
    bool Bonus)
{
    public bool Running => Outcome == SessionOutcome.Running;
}

// Kind null for a step this client doesn't know yet. Empty: nothing to do in it, the review until M4. Questions: the
// size of the quiz, if Planning gives one.
public sealed record SessionStep(SessionStepKind? Kind, bool Empty, int? Questions);

public enum SessionStepKind
{
    Review,
    IntroductionUnit,
    Quiz,
}

public enum SessionOutcome
{
    Running,
    Aborted,
    Completed,
}