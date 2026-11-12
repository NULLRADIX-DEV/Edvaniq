namespace Edvaniq.Client.Core;

// The days from today on as Planning plans them (#379), through the web host. Day 0 is today's plan.
public interface IWeekPlan
{
    Task<CallOutcome<WeekPlan>> GetAsync(CancellationToken cancellationToken = default);
}

// Days: 7 without an exam, with one only those before the exam day, at least today. A day without blocks has an empty
// list. Empty is set only while no day has blocks, the rest as in TodayPlan.
public sealed record WeekPlan(
    IReadOnlyList<PlanDay> Days,
    PlanEmpty? Empty,
    bool MasteryAvailable,
    IReadOnlyList<Guid> ShortenedTopicIds,
    IReadOnlyList<Guid> NotReadyTopicIds);

public sealed record PlanDay(DateOnly StudyDay, IReadOnlyList<PlanBlock> Blocks);