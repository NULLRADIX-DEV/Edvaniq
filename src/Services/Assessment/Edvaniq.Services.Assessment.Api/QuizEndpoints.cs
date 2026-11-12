using Edvaniq.Services.Assessment.Application;

namespace Edvaniq.Services.Assessment.Api;

// The quiz of a block of the plan (#393). The block id comes from Planning, Assessment only uses it as a key with the
// owner from the token. Each question is an attempt answered at /attempts/{id}/answer.
internal static class QuizEndpoints
{
    public static IEndpointRouteBuilder MapQuizEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/quizzes", async (QuizRequest request, BlockQuizOpener quizzes, CancellationToken cancellationToken) =>
        {
            if (request.TopicId is not { } topicId)
            {
                return Invalid(nameof(request.TopicId), "Es fehlt das Thema.");
            }

            if (request.BlockId is not { } blockId)
            {
                return Invalid(nameof(request.BlockId), "Es fehlt der Block.");
            }

            if (request.Size is not ({ } size and > 0 and <= BlockQuizOpener.MaxSize))
            {
                return Invalid(nameof(request.Size), $"Die Zahl der Fragen fehlt oder liegt nicht zwischen 1 und {BlockQuizOpener.MaxSize}.");
            }

            var opened = await quizzes.OpenAsync(topicId, blockId, size, cancellationToken);
            return opened.Outcome switch
            {
                QuizOutcome.UnknownTopic => Results.NotFound(),
                QuizOutcome.NoQuestions => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Zu diesem Thema gibt es noch keine Fragen."),
                QuizOutcome.OtherTopic => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Zu diesem Block gibt es schon ein Quiz zu einem anderen Thema."),
                _ => Results.Ok(new QuizResponse(
                    opened.Quiz!.Id,
                    opened.Quiz.BlockId,
                    opened.Quiz.TopicId,
                    [.. opened.Questions!.Select(entry => QuizQuestionResponse.From(AskedResponse.From(entry.Attempt, entry.Question), entry.Attempt.Correct))])),
            };
        });

        return app;
    }

    private static IResult Invalid(string field, string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });
}

// Size: how many questions the quiz gets when it is made, ignored once it is there.
internal sealed record QuizRequest(Guid? TopicId, Guid? BlockId, int? Size);

// The questions in the same order on every call.
internal sealed record QuizResponse(Guid Id, Guid BlockId, Guid TopicId, IReadOnlyList<QuizQuestionResponse> Questions);

// A question as POST /attempts gives it, without the solution. Answered: the user gave an answer already, Correct says
// how it was graded, null while it is open.
internal sealed record QuizQuestionResponse(
    Guid Id, Guid TopicId, Guid QuestionId, string Type, string Text, IReadOnlyList<string> Options, int Difficulty, bool Answered, bool? Correct)
{
    public static QuizQuestionResponse From(AskedResponse asked, bool? correct) =>
        new(asked.Id, asked.TopicId, asked.QuestionId, asked.Type, asked.Text, asked.Options, asked.Difficulty, correct is not null, correct);
}