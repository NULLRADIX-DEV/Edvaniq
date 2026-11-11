using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// Puts a question of a topic to the user and grades the answer. The answer and its learning record are stored by the
// same save, and an attempt is answered once: whatever comes in after that gets the stored result back.
public sealed class Quiz(ITopicSource topics, IAttempts attempts, IEventOutbox outbox, TimeProvider time)
{
    public async Task<Asked> AskAsync(Guid topicId, CancellationToken cancellationToken)
    {
        if (await topics.FindAsync(topicId, cancellationToken) is not { } source)
        {
            return new(AskOutcome.UnknownTopic, null, null);
        }

        var questions = source.DocumentId is { } documentId
            ? await attempts.QuestionsOfAsync(documentId, source.Position, cancellationToken)
            : [];
        if (questions.Count == 0)
        {
            return new(AskOutcome.NoQuestions, null, null);
        }

        // Leaving out the questions seen last is not part of this yet.
        var question = questions[Random.Shared.Next(questions.Count)];
        var attempt = Attempt.Ask(question, topicId, time.GetUtcNow().UtcDateTime);
        attempts.Add(attempt);
        await attempts.SaveAsync(cancellationToken);
        return new(AskOutcome.Asked, attempt, question);
    }

    public async Task<Graded> AnswerAsync(Guid attemptId, int option, int responseTimeMs, CancellationToken cancellationToken)
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

        if (!Attempt.IsOption(question, option))
        {
            return new(GradeOutcome.InvalidOption, null, null);
        }

        attempt.Answer(question, option, responseTimeMs, time.GetUtcNow().UtcDateTime);
        outbox.Add(new AnswerRecorded(
            AnswerRecorded.CurrentVersion,
            attempt.OwnerId,
            attempt.TopicId,
            question.Id,
            SessionId: null,
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

public sealed record Asked(AskOutcome Outcome, Attempt? Attempt, Question? Question);

public enum GradeOutcome
{
    Graded,
    NotFound,
    InvalidOption,
}

public sealed record Graded(GradeOutcome Outcome, Attempt? Attempt, Question? Question);