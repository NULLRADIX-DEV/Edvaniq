namespace Edvaniq.Client.Core;

// Two or three questions for a new topic from Assessment, through the web host (#355). Each is an attempt answered
// through IQuiz like any other.
public interface ICheckQuestions
{
    // Every call makes new attempts, so a unit calls it once. Ready false while the topic has no questions yet: a status,
    // not an error. NotFound for a topic the user doesn't have. SessionId is the session of the unit, if there is one.
    Task<CallOutcome<CheckQuestions>> StartAsync(Guid topicId, Guid? sessionId, CancellationToken cancellationToken = default);
}

// Questions has two or three when Ready, none otherwise.
public sealed record CheckQuestions(Guid TopicId, bool Ready, IReadOnlyList<AskedQuestion> Questions);