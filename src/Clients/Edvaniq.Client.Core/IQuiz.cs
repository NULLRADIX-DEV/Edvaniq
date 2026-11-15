namespace Edvaniq.Client.Core;

// A question of a topic from Assessment and the grade of its answer, through the web host.
public interface IQuiz
{
    // A new attempt with a question of the topic, without its solution. NotReady while the topic has no questions yet,
    // NotFound for a topic the user doesn't have.
    Task<CallOutcome<AskedQuestion>> AskAsync(Guid topicId, CancellationToken cancellationToken = default);

    // Grades the answer once. Any further call for the same attempt brings back the stored grade, whatever option it sends.
    Task<CallOutcome<GradedAnswer>> AnswerAsync(Guid attemptId, int option, int responseTimeMs, CancellationToken cancellationToken = default);

    // The same for a multiple choice question: all options ticked, sent without doubles and in order, so a retry sends the
    // same body.
    Task<CallOutcome<GradedAnswer>> AnswerAsync(Guid attemptId, IReadOnlyList<int> options, int responseTimeMs, CancellationToken cancellationToken = default);

    // "Show solution": answers the attempt as wrong and brings the solution. Like an answer it counts only if it's first.
    Task<CallOutcome<GradedAnswer>> RevealAsync(Guid attemptId, int responseTimeMs, CancellationToken cancellationToken = default);
}

// Id is the attempt the answer goes to. Type is one of QuestionTypes, a question without one is single choice.
public sealed record AskedQuestion(
    Guid Id, Guid TopicId, Guid QuestionId, string Text, IReadOnlyList<string> Options, int Difficulty, string? Type = null)
{
    public bool IsMultipleChoice => Type == QuestionTypes.MultipleChoice;

    // A type the page doesn't know can't be answered fairly, it says so instead.
    public bool IsKnownType => Type is null or QuestionTypes.SingleChoice or QuestionTypes.MultipleChoice or QuestionTypes.TrueFalse;
}

// As Assessment names them. True/false comes as two options like single choice.
public static class QuestionTypes
{
    public const string SingleChoice = "singleChoice";
    public const string MultipleChoice = "multipleChoice";
    public const string TrueFalse = "trueFalse";
}

// Option or Options is what the attempt was graded with (single or multiple choice), none if the solution was shown
// (Revealed). Answer is the lowest right option, Answers all of them. Assessment has no partial credit: a multiple choice
// answer is right only with exactly the right options.
public sealed record GradedAnswer(
    Guid Id,
    int? Option,
    bool Correct,
    int Answer,
    string Explanation,
    int ResponseTimeMs,
    bool Revealed = false,
    IReadOnlyList<int>? Options = null,
    IReadOnlyList<int>? Answers = null)
{
    public IReadOnlyList<int> RightOptions => Answers is { Count: > 0 } answers ? answers : [Answer];

    public IReadOnlyList<int> Chosen => Options ?? (Option is { } option ? [option] : []);

    // Equal by the options themselves, not by the lists that hold them.
    public bool Equals(GradedAnswer? other) =>
        other is not null
        && (Id, Option, Correct, Answer, Explanation, ResponseTimeMs, Revealed)
            == (other.Id, other.Option, other.Correct, other.Answer, other.Explanation, other.ResponseTimeMs, other.Revealed)
        && Chosen.SequenceEqual(other.Chosen)
        && RightOptions.SequenceEqual(other.RightOptions);

    public override int GetHashCode() => HashCode.Combine(Id, Option, Correct, Answer, Explanation, ResponseTimeMs, Revealed);
}