using System.Text.Json;

using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Api;

// Asking a question of a topic and answering it. The solution only comes with the grade, and showing the solution is an
// answer too, graded wrong. The owner never appears in a request: it comes from the token, and someone else's topic or
// attempt looks like none.
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
            var reveal = request.ShowSolution == true;
            if ((request.Option is not null ? 1 : 0) + (request.Options is not null ? 1 : 0) + (reveal ? 1 : 0) != 1)
            {
                return Invalid(nameof(request.Option), "Gib genau eins an: eine Antwort, mehrere Antworten oder „Lösung anzeigen“.");
            }

            if (request.ResponseTimeMs is not ({ } responseTimeMs and >= 0))
            {
                return Invalid(nameof(request.ResponseTimeMs), "Die Antwortzeit fehlt oder ist negativ.");
            }

            var graded = reveal
                ? await quiz.RevealAsync(id, responseTimeMs, cancellationToken)
                : await quiz.AnswerAsync(id, request.Options ?? [request.Option!.Value], responseTimeMs, cancellationToken);
            return graded.Outcome switch
            {
                GradeOutcome.NotFound => Results.NotFound(),
                GradeOutcome.InvalidOption => Invalid(nameof(request.Option), "Diese Antwort gibt es bei der Frage nicht."),
                _ => Results.Ok(GradedResponse.From(graded.Attempt!, graded.Question!)),
            };
        });

        // Two or three questions for a new topic, each an attempt answered at /attempts/{id}/answer. A pool that isn't
        // there yet is a status, not an error: the unit can go on without it.
        app.MapPost("/check-questions", async (AskRequest request, Quiz quiz, CancellationToken cancellationToken) =>
        {
            if (request.TopicId is not { } topicId)
            {
                return Invalid(nameof(request.TopicId), "Es fehlt das Thema.");
            }

            var check = await quiz.CheckAsync(topicId, cancellationToken);
            return check.Outcome switch
            {
                CheckOutcome.UnknownTopic => Results.NotFound(),
                _ => Results.Ok(new CheckResponse(
                    topicId,
                    check.Outcome == CheckOutcome.Ready ? "ready" : "notReady",
                    [.. check.Asked.Select(asked => AskedResponse.From(asked.Attempt, asked.Question))])),
            };
        });

        return app;
    }

    private static IResult Invalid(string field, string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });

    internal static string Name(QuestionType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());
}

internal sealed record AskRequest(Guid? TopicId);

// Without the solution, that comes with the grade. Type is singleChoice, multipleChoice or trueFalse.
internal sealed record AskedResponse(Guid Id, Guid TopicId, Guid QuestionId, string Type, string Text, IReadOnlyList<string> Options, int Difficulty)
{
    public static AskedResponse From(Attempt attempt, Question question) =>
        new(attempt.Id, attempt.TopicId, question.Id, AttemptEndpoints.Name(question.Type), question.Text, question.Options, question.Difficulty);
}

// Status is ready with two or three questions, or notReady with none.
internal sealed record CheckResponse(Guid TopicId, string Status, IReadOnlyList<AskedResponse> Questions);

// Exactly one of Option (single choice, true/false), Options (multiple choice, order doesn't matter) or ShowSolution.
// ResponseTimeMs: the time the client measured from showing the question to the answer, in milliseconds.
internal sealed record AnswerRequest(int? Option, IReadOnlyList<int>? Options, bool? ShowSolution, int? ResponseTimeMs);

// Id is the answerId of the learning record. Option or Options is what was chosen, both null when the solution was
// shown (Revealed). Answers are all right options, Answer is the lowest of them.
internal sealed record GradedResponse(
    Guid Id,
    Guid TopicId,
    Guid QuestionId,
    string Type,
    int? Option,
    IReadOnlyList<int>? Options,
    bool Revealed,
    bool Correct,
    int Answer,
    IReadOnlyList<int> Answers,
    string Explanation,
    int ResponseTimeMs,
    DateTime AnsweredAt)
{
    public static GradedResponse From(Attempt attempt, Question question) =>
        new(
            attempt.Id,
            attempt.TopicId,
            question.Id,
            AttemptEndpoints.Name(question.Type),
            attempt.Option,
            attempt.Options,
            attempt.Revealed,
            attempt.Correct!.Value,
            question.Answer,
            question.RightOptions,
            question.Explanation,
            attempt.ResponseTimeMs!.Value,
            attempt.AnsweredAt!.Value);
}