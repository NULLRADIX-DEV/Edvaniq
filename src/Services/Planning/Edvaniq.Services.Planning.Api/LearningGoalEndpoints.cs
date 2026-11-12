using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// The learning goals of the user from the token. The owner never appears in a request: it comes from the token, and the
// owner filter applies it.
internal static class LearningGoalEndpoints
{
    public static IEndpointRouteBuilder MapLearningGoalEndpoints(this IEndpointRouteBuilder app)
    {
        var goals = app.MapGroup("/learning-goals");

        // POST, because the first call creates it. Content asks for it when a text comes in (#215), with the token of the
        // user who sent the text, and keeps level and output language for the prompts (#290). A default goal nobody took
        // over has neither, Content picks its own default then.
        goals.MapPost("/default", async (ILearningGoals goals, CancellationToken cancellationToken) =>
            DefaultLearningGoalResponse.From(await goals.DefaultAsync(cancellationToken)));

        goals.MapPost("/", async (NewLearningGoal request, ILearningGoals goals, TimeProvider time, CancellationToken cancellationToken) =>
        {
            var fields = LearningGoalFields.Parse(
                request.Kind,
                request.Name,
                request.ExamType,
                request.ExamDate,
                request.Level,
                request.OutputLanguage,
                request.Goal,
                time.GetUtcNow().UtcDateTime,
                out var errors);
            if (fields is null)
            {
                return Results.ValidationProblem(errors);
            }

            var goal = await goals.CreateAsync(fields, cancellationToken);
            return Results.Created($"/learning-goals/{goal.Id}", LearningGoalResponse.From(goal));
        });

        goals.MapGet("/", async (ILearningGoals goals, CancellationToken cancellationToken) =>
            (await goals.ListAsync(cancellationToken)).Select(LearningGoalResponse.From));

        // Someone else's goal and one that does not exist look the same: 404.
        goals.MapGet("/{id:guid}", async (Guid id, ILearningGoals goals, CancellationToken cancellationToken) =>
            await goals.FindAsync(id, cancellationToken) is { } goal
                ? Results.Ok(LearningGoalResponse.From(goal))
                : Results.NotFound());

        return app;
    }
}

internal sealed record DefaultLearningGoalResponse(Guid Id, string? Level, string? OutputLanguage)
{
    public static DefaultLearningGoalResponse From(LearningGoal goal) => new(goal.Id, goal.Level?.ToString(), goal.OutputLanguage);
}

// Strings, so an unknown value gets a message at its field. ExamDate as yyyy-MM-dd.
internal sealed record NewLearningGoal(
    string? Kind,
    string? Name,
    string? ExamType,
    string? ExamDate,
    string? Level,
    string? OutputLanguage,
    string? Goal);

// InPlan marks the goal that plan, sessions and Content follow for now: the one that took over the default goal. Added
// last, Content reads this answer and leaves unknown fields alone.
internal sealed record LearningGoalResponse(
    Guid Id,
    string Kind,
    string Name,
    string? ExamType,
    DateOnly? ExamDate,
    string? Level,
    string OutputLanguage,
    string? Goal,
    DateTime CreatedAt,
    bool InPlan)
{
    public static LearningGoalResponse From(LearningGoal goal) =>
        new(
            goal.Id,
            goal.Kind!.Value.ToString(),
            goal.Name!,
            goal.ExamType?.ToString(),
            goal.ExamDate,
            goal.Level?.ToString(),
            goal.OutputLanguage!,
            goal.Goal,
            goal.CreatedAt,
            goal.Id == LearningGoal.DefaultIdFor(goal.OwnerId));
}