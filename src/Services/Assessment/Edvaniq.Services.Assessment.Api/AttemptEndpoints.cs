using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Api;

// Asking a question of a topic and answering it. The solution only comes with the grade. The owner never appears in a
// request: it comes from the token, and someone else's topic or attempt looks like none.
internal static class AttemptEndpoints
{
    public static IEndpointRouteBuilder MapAttemptEndpoints(this IEndpointRouteBuilder app)
    {
        var attempts = app.MapGroup("/attempts");

        attempts.MapPost("/", async (AskRequest request, Quiz quiz, CancellationToken cancellationToken) =>
        {
            if (request.TopicId is not { } topicId)
            {
                return Invalid(nameof(request.TopicId), "Es fehlt das Thema.");
            }

            var asked = await quiz.AskAsync(topicId, cancellationToken);
            return asked.Outcome switch
            {
                AskOutcome.UnknownTopic => Results.NotFound(),

                // The questions come some time after the topics, the client says so and asks again later.
                AskOutcome.NoQuestions => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Zu diesem Thema gibt es noch keine Fragen."),
                _ => Results.Created($"/attempts/{asked.Attempt!.Id}", AskedResponse.From(asked.Attempt, asked.Question!)),
            };
        });

        attempts.MapPost("/{id:guid}/answer", async (Guid id, AnswerRequest request, Quiz quiz, CancellationToken cancellationToken) =>
        {
            if (request.Option is not { } option)
            {
                return Invalid(nameof(request.Option), "Es fehlt die gewählte Antwort.");
            }

            if (request.ResponseTimeMs is not ({ } responseTimeMs and >= 0))
            {
                return Invalid(nameof(request.ResponseTimeMs), "Die Antwortzeit fehlt oder ist negativ.");
            }

            var graded = await quiz.AnswerAsync(id, option, responseTimeMs, cancellationToken);
            return graded.Outcome switch
            {
                GradeOutcome.NotFound => Results.NotFound(),
                GradeOutcome.InvalidOption => Invalid(nameof(request.Option), "Diese Antwort gibt es bei der Frage nicht."),
                _ => Results.Ok(GradedResponse.From(graded.Attempt!, graded.Question!)),
            };
        });

        return app;
    }

    private static IResult Invalid(string field, string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });
}

internal sealed record AskRequest(Guid? TopicId);

// Without the solution, that comes with the grade.
internal sealed record AskedResponse(Guid Id, Guid TopicId, Guid QuestionId, string Text, IReadOnlyList<string> Options, int Difficulty)
{
    public static AskedResponse From(Attempt attempt, Question question) =>
        new(attempt.Id, attempt.TopicId, question.Id, question.Text, question.Options, question.Difficulty);
}

// ResponseTimeMs: the time the client measured from showing the question to the answer, in milliseconds.
internal sealed record AnswerRequest(int? Option, int? ResponseTimeMs);

// Id is the answerId of the learning record. Answer is the index of the right option.
internal sealed record GradedResponse(
    Guid Id,
    Guid TopicId,
    Guid QuestionId,
    int Option,
    bool Correct,
    int Answer,
    string Explanation,
    int ResponseTimeMs,
    DateTime AnsweredAt)
{
    public static GradedResponse From(Attempt attempt, Question question) =>
        new(
            attempt.Id,
            attempt.TopicId,
            question.Id,
            attempt.Option!.Value,
            attempt.Correct!.Value,
            question.Answer,
            question.Explanation,
            attempt.ResponseTimeMs!.Value,
            attempt.AnsweredAt!.Value);
}