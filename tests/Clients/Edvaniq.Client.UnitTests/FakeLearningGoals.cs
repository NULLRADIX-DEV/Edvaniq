using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers each kind of call with its queue in order and repeats the last answer after that. Remembers what it was sent.
internal sealed class FakeLearningGoals : ILearningGoals
{
    private readonly Queue<CallOutcome<IReadOnlyList<LearningGoalInfo>>> lists = new();
    private readonly Queue<CallOutcome<LearningGoalInfo>> creates = new();

    public List<NewLearningGoal> Created { get; } = [];

    public FakeLearningGoals List(params LearningGoalInfo[] goals) => List(CallOutcome<IReadOnlyList<LearningGoalInfo>>.Ok(goals));

    public FakeLearningGoals List(CallOutcome<IReadOnlyList<LearningGoalInfo>> outcome)
    {
        lists.Enqueue(outcome);
        return this;
    }

    public FakeLearningGoals Create(CallOutcome<LearningGoalInfo> outcome)
    {
        creates.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<IReadOnlyList<LearningGoalInfo>>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Next(lists, CallOutcome<IReadOnlyList<LearningGoalInfo>>.Ok([])));

    // A goal made of what was sent, unless the test queued an answer.
    public Task<CallOutcome<LearningGoalInfo>> CreateAsync(NewLearningGoal goal, CancellationToken cancellationToken = default)
    {
        Created.Add(goal);
        var made = new LearningGoalInfo(
            Guid.NewGuid(),
            goal.Kind,
            goal.Name,
            goal.ExamType,
            goal.ExamDate is null ? null : DateOnly.Parse(goal.ExamDate, System.Globalization.CultureInfo.InvariantCulture),
            goal.Level,
            goal.OutputLanguage,
            goal.Goal,
            DateTime.UnixEpoch,
            InPlan: Created.Count == 1);
        return Task.FromResult(Next(creates, CallOutcome<LearningGoalInfo>.Ok(made)));
    }

    private static T Next<T>(Queue<T> queue, T otherwise) =>
        queue.Count > 1 ? queue.Dequeue() : queue.Count == 1 ? queue.Peek() : otherwise;
}