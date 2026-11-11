namespace Edvaniq.Client.Core;

// A question of a topic from Assessment and the grade of its answer, through the web host.
public interface IQuiz
{
    // A new attempt with a question of the topic, without its solution. NotReady while the topic has no questions yet,
    // NotFound for a topic the user doesn't have.
    Task<CallOutcome<AskedQuestion>> AskAsync(Guid topicId, CancellationToken cancellationToken = default);

    // Grades the answer once. Any further call for the same attempt brings back the stored grade, whatever option it sends.
    Task<CallOutcome<GradedAnswer>> AnswerAsync(Guid attemptId, int option, int responseTimeMs, CancellationToken cancellationToken = default);
}

// Id is the attempt the answer goes to. Single choice: exactly one of the options is right.
public sealed record AskedQuestion(Guid Id, Guid TopicId, Guid QuestionId, string Text, IReadOnlyList<string> Options, int Difficulty);

// Option is the one the attempt was graded with, Answer the index of the right one.
public sealed record GradedAnswer(Guid Id, int Option, bool Correct, int Answer, string Explanation, int ResponseTimeMs);