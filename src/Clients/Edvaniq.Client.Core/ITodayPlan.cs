namespace Edvaniq.Client.Core;

// Today's plan of the user from Planning, through the web host. Planning only knows topic ids, the names come from
// Knowledge.
public interface ITodayPlan
{
    Task<CallOutcome<TodayPlan>> GetAsync(CancellationToken cancellationToken = default);
}

// Empty is set only while there are no blocks, and stays null for a reason this client doesn't know yet.
// MasteryAvailable false: Planning didn't get the mastery, so weaker topics don't come first. Shortened: topics without
// learn time this week, the time is too short. NotReady: topics left out until their questions are there.
// ExamDate: the exam of the goal in the plan. DaysToExam: Planning's count from the study day, 0 on the exam day,
// null without an exam or once it is over (#382).
public sealed record TodayPlan(
    DateOnly StudyDay,
    IReadOnlyList<PlanBlock> Blocks,
    PlanEmpty? Empty,
    bool MasteryAvailable,
    IReadOnlyList<Guid> ShortenedTopicIds,
    IReadOnlyList<Guid> NotReadyTopicIds,
    DateOnly? ExamDate = null,
    int? DaysToExam = null);

// Kind is null for a kind this client doesn't know yet.
public sealed record PlanBlock(Guid TopicId, int Minutes, BlockKind? Kind);

public enum BlockKind
{
    Learn,
    Review,
}

public enum PlanEmpty
{
    NoTopics,
    QuestionsPending,
    AllMastered,
}