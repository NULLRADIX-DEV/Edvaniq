using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// Puts a question of a topic to the user and grades the answer. The answer and its learning record are stored by the
// same save, and an attempt is answered once: whatever comes in after that gets the stored result back.
// The topic must be one of the user's from TopicsChanged, someone else's looks like none.
public sealed class Quiz(IAttempts attempts, IEventOutbox outbox, TimeProvider time)
{
    public async Task<Asked> AskAsync(Guid topicId, CancellationToken cancellationToken)
    {
        if (!await attempts.KnowsTopicAsync(topicId, cancellationToken))
        {
            return new(AskOutcome.UnknownTopic, null, null);
        }

        var questions = await attempts.QuestionsOfAsync(topicId, cancellationToken);
        if (questions.Count == 0)
        {
            return new(AskOutcome.NoQuestions, null, null);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var selection = QuestionSelection.Pick(questions, await attempts.LastAnsweredAsync([topicId], cancellationToken), 1, now, Random.Shared);
        var question = selection.Questions[0];
        var attempt = Attempt.Ask(question, topicId, now);
        attempts.Add(attempt);
        await attempts.SaveAsync(cancellationToken);
        return new(AskOutcome.Asked, attempt, question, selection.PoolTooSmall);
    }

    // Asks the check questions in one save. Asking again brings a new set and leaves the open ones unanswered, keeping
    // one unit together across reloads is the session's job in Planning. The session goes with every answer, so
    // LearningEngine counts the check questions once the session has introduced the topic.
    public Task<Checked> CheckAsync(Guid topicId, CancellationToken cancellationToken) =>
        CheckAsync(topicId, null, cancellationToken);

    public async Task<Checked> CheckAsync(Guid topicId, Guid? sessionId, CancellationToken cancellationToken)
    {
        if (!await attempts.KnowsTopicAsync(topicId, cancellationToken))
        {
            return new(CheckOutcome.UnknownTopic, []);
        }

        if (CheckQuestions.Pick(await attempts.QuestionsOfAsync(topicId, cancellationToken), Random.Shared) is not { } picked)
        {
            return new(CheckOutcome.NotReady, []);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var asked = picked.Select(question => (Attempt.Ask(question, topicId, now, sessionId), question)).ToList();
        foreach (var (attempt, _) in asked)
        {
            attempts.Add(attempt);
        }

        await attempts.SaveAsync(cancellationToken);
        return new(CheckOutcome.Ready, asked);
    }

    public Task<Graded> AnswerAsync(Guid attemptId, int option, int responseTimeMs, CancellationToken cancellationToken) =>
        AnswerAsync(attemptId, [option], responseTimeMs, cancellationToken);

    public Task<Graded> AnswerAsync(Guid attemptId, IReadOnlyCollection<int> chosen, int responseTimeMs, CancellationToken cancellationToken) =>
        GradeAsync(attemptId, chosen, responseTimeMs, cancellationToken);

    // "Show solution": graded wrong, and like an answer only if it comes first.
    public Task<Graded> RevealAsync(Guid attemptId, int responseTimeMs, CancellationToken cancellationToken) =>
        GradeAsync(attemptId, null, responseTimeMs, cancellationToken);

    private async Task<Graded> GradeAsync(Guid attemptId, IReadOnlyCollection<int>? chosen, int responseTimeMs, CancellationToken cancellationToken)
    {
        if (await attempts.FindAsync(attemptId, cancellationToken) is not { } attempt
            || await attempts.FindQuestionAsync(attempt.QuestionId, cancellationToken) is not { } question)
        {
            return new(GradeOutcome.NotFound, null, null);
        }

        if (attempt.Answered)
        {
            return new(GradeOutcome.Graded, attempt, question);
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (chosen is null)
        {
            attempt.Reveal(question, responseTimeMs, now);
        }
        else if (Attempt.IsChoice(question, chosen))
        {
            attempt.Answer(question, chosen, responseTimeMs, now);
        }
        else
        {
            return new(GradeOutcome.InvalidOption, null, null);
        }

        outbox.Add(new AnswerRecorded(
            AnswerRecorded.CurrentVersion,
            attempt.OwnerId,
            attempt.TopicId,
            question.Id,
            attempt.SessionId,
            attempt.Id,
            attempt.Correct!.Value,
            question.Difficulty,
            attempt.ResponseTimeMs!.Value,
            Confidence: null,
            Mode.Practice,
            attempt.AnsweredAt!.Value));

        if (await attempts.SaveAsync(cancellationToken))
        {
            return new(GradeOutcome.Graded, attempt, question);
        }

        // Another request was first, its answer counts.
        return await attempts.FindAsync(attemptId, cancellationToken) is { Answered: true } stored
            ? new(GradeOutcome.Graded, stored, question)
            : new(GradeOutcome.NotFound, null, null);
    }
}

public enum AskOutcome
{
    Asked,
    UnknownTopic,
    NoQuestions,
}

// Repeated: the user answered the question recently, the topic has no other left (#387).
public sealed record Asked(AskOutcome Outcome, Attempt? Attempt, Question? Question, bool Repeated = false);

public enum GradeOutcome
{
    Graded,
    NotFound,
    InvalidOption,
}

public sealed record Graded(GradeOutcome Outcome, Attempt? Attempt, Question? Question);