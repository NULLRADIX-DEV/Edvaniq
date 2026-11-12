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
                    [.. opened.Questions!.Select(entry => QuizQuestionResponse.From(AskedResponse.From(entry.Attempt, entry.Question), entry.Attempt.Correct))],
                    opened.Quiz.PoolTooSmall ?? false)),
            };
        });

        // A quiz of the weaknesses of a learning goal (#387). New questions on every call, each answered at
        // /attempts/{id}/answer.
        app.MapPost("/quizzes/weaknesses", async (WeaknessQuizRequest request, WeaknessQuiz quiz, CancellationToken cancellationToken) =>
        {
            if (request.LearningGoalId is not { } learningGoalId)
            {
                return Invalid(nameof(request.LearningGoalId), "Es fehlt das Lernziel.");
            }

            if (request.Size is not ({ } size and > 0 and <= WeaknessQuiz.MaxSize))
            {
                return Invalid(nameof(request.Size), $"Die Zahl der Fragen fehlt oder liegt nicht zwischen 1 und {WeaknessQuiz.MaxSize}.");
            }

            var asked = await quiz.AskAsync(learningGoalId, size, cancellationToken);
            return asked.Outcome switch
            {
                WeaknessQuizOutcome.Unavailable => Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Deine Schwächen lassen sich gerade nicht abrufen. Versuch es gleich noch einmal."),
                WeaknessQuizOutcome.NoWeaknesses => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Du hast in diesem Lernziel gerade keine Schwächen."),
                WeaknessQuizOutcome.NoQuestions => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Zu deinen Schwächen gibt es noch keine Fragen."),
                _ => Results.Ok(new WeaknessQuizResponse(
                    [.. asked.Questions!.Select(entry => QuizQuestionResponse.From(AskedResponse.From(entry.Attempt, entry.Question), null))],
                    asked.PoolTooSmall)),
            };
        });

        return app;
    }

    private static IResult Invalid(string field, string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });
}

// Size: how many questions the quiz gets when it is made, ignored once it is there.
internal sealed record QuizRequest(Guid? TopicId, Guid? BlockId, int? Size);

// The questions in the same order on every call. PoolTooSmall: the topic had too few questions when the quiz was made,
// so it repeats one answered recently or has fewer than asked for (#387).
internal sealed record QuizResponse(Guid Id, Guid BlockId, Guid TopicId, IReadOnlyList<QuizQuestionResponse> Questions, bool PoolTooSmall);

internal sealed record WeaknessQuizRequest(Guid? LearningGoalId, int? Size);

// PoolTooSmall as for a block's quiz.
internal sealed record WeaknessQuizResponse(IReadOnlyList<QuizQuestionResponse> Questions, bool PoolTooSmall);

// A question as POST /attempts gives it, without the solution. Answered: the user gave an answer already, Correct says
// how it was graded, null while it is open.
internal sealed record QuizQuestionResponse(
    Guid Id, Guid TopicId, Guid QuestionId, string Type, string Text, IReadOnlyList<string> Options, int Difficulty, bool Answered, bool? Correct)
{
    public static QuizQuestionResponse From(AskedResponse asked, bool? correct) =>
        new(asked.Id, asked.TopicId, asked.QuestionId, asked.Type, asked.Text, asked.Options, asked.Difficulty, correct is not null, correct);
}