using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Api;

// How many usable questions each of the user's topics has and whether its pool is ready, for Planning and the client.
// The owner comes from the token, someone else's topic looks like none.
internal static class QuestionPoolEndpoints
{
    public static IEndpointRouteBuilder MapQuestionPoolEndpoints(this IEndpointRouteBuilder app)
    {
        var pools = app.MapGroup("/question-pools");

        pools.MapGet("/{topicId:guid}", async (Guid topicId, IQuestionPools questionPools, CancellationToken cancellationToken) =>
            await questionPools.FindAsync(topicId, cancellationToken) is { } pool
                ? Results.Ok(new QuestionPoolResponse(pool.TopicId, pool.Questions, Status(pool.Questions), QuestionPool.MinQuestions))
                : Results.NotFound());

        // All of the user's topics, or those of one learning goal. A goal that isn't the user's gives an empty list, like
        // one without topics: this service doesn't own learning goals. Parsed by hand, so a broken id is a 400 with its field.
        pools.MapGet("/", async (string? learningGoalId, IQuestionPools questionPools, CancellationToken cancellationToken) =>
        {
            Guid? goal = null;
            if (learningGoalId is not null)
            {
                if (!Guid.TryParse(learningGoalId, out var parsed))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["learningGoalId"] = ["Must be a GUID."] });
                }

                goal = parsed;
            }

            var topics = await questionPools.ListAsync(goal, cancellationToken);
            return Results.Ok(new QuestionPoolListResponse(
                QuestionPool.MinQuestions,
                [.. topics.Select(topic => new QuestionPoolListEntry(topic.TopicId, topic.Questions, Status(topic.Questions)))]));
        });

        return app;
    }

    // The same words as the status of /check-questions.
    private static string Status(int questions) => QuestionPool.IsReady(questions) ? "ready" : "notReady";
}

// Questions is the count of usable ones, Status ready or notReady, MinQuestions how many make a pool ready.
internal sealed record QuestionPoolResponse(Guid TopicId, int Questions, string Status, int MinQuestions);

// An object, not a bare list, so fields can be added later.
internal sealed record QuestionPoolListResponse(int MinQuestions, IReadOnlyList<QuestionPoolListEntry> Topics);

internal sealed record QuestionPoolListEntry(Guid TopicId, int Questions, string Status);