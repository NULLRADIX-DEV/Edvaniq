using Edvaniq.BuildingBlocks.Web;
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

        // Changes everything but the kind (#516), with the rules of POST. A kind in the body is ignored, the request has no
        // such field. The topics are Knowledge's: hiding and showing them there changes the plan (#286). Someone else's
        // goal and the default goal nobody took over are 404.
        goals.MapPut("/{id:guid}", async (Guid id, EditedLearningGoal request, ILearningGoals goals, TimeProvider time, CancellationToken cancellationToken) =>
        {
            if (await goals.FindForChangeAsync(id, cancellationToken) is not { } goal)
            {
                return Results.NotFound();
            }

            var now = time.GetUtcNow().UtcDateTime;
            var fields = LearningGoalFields.Parse(
                goal.Kind!.Value.ToString(),
                request.Name,
                request.ExamType,
                request.ExamDate,
                request.Level,
                request.OutputLanguage,
                request.Goal,
                now,
                out var errors);
            if (fields is null)
            {
                return Results.ValidationProblem(errors);
            }

            goal.Update(fields, now);
            await goals.SaveAsync(cancellationToken);
            return Results.Ok(LearningGoalResponse.From(goal));
        });

        // Only what Content needs to take a document into the goal, also for the default goal: nothing to identify the
        // learner by, no name, no free text. Someone else's goal is 404 as well.
        goals.MapGet("/{id:guid}/profile", async (Guid id, ILearningGoals goals, CancellationToken cancellationToken) =>
            await goals.FindWithDefaultAsync(id, cancellationToken) is { } goal
                ? Results.Ok(LearningGoalProfileResponse.From(goal))
                : Results.NotFound());

        // Content's worker asks before every AI step of a document, so a change of the goal counts at the next analysis.
        // Only Content's internal token gets in, no user. No gateway route leads here.
        app.MapGet("/internal/learning-goals/{id:guid}/profile", async (Guid id, IGoalProfiles profiles, CancellationToken cancellationToken) =>
                await profiles.FindAsync(id, cancellationToken) is { } goal
                    ? Results.Ok(InternalGoalProfileResponse.From(goal))
                    : Results.NotFound())
            .RequireInternalClient(ContentClient);

        return app;
    }

    public const string ContentClient = "content";
}

// Content reads this (PlanningLearningGoals), keep the fields.
internal sealed record LearningGoalProfileResponse(Guid Id, string? Level, string? OutputLanguage)
{
    public static LearningGoalProfileResponse From(LearningGoal goal) => new(goal.Id, goal.Level?.ToString(), goal.OutputLanguage);
}

// Content's worker reads this (PlanningGoalProfiles), keep the fields.
internal sealed record InternalGoalProfileResponse(string? Level, string? OutputLanguage)
{
    public static InternalGoalProfileResponse From(LearningGoal goal) => new(goal.Level?.ToString(), goal.OutputLanguage);
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

// The fields of NewLearningGoal without the kind, which can't change.
internal sealed record EditedLearningGoal(
    string? Name,
    string? ExamType,
    string? ExamDate,
    string? Level,
    string? OutputLanguage,
    string? Goal);

// InPlan marks the goal that plan, sessions and Content follow for now: the one that took over the default goal.
// UpdatedAt: the last change by the user, null until there was one (#516). Both added last, Content reads this answer
// and leaves unknown fields alone.
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
    bool InPlan,
    DateTime? UpdatedAt)
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
            goal.Id == LearningGoal.DefaultIdFor(goal.OwnerId),
            goal.UpdatedAt);
}