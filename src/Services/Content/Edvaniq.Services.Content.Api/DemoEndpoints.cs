using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.Api;

// The demo learning goal (#411). The client asks GET /demo first. Without a demo it creates a learning goal in Planning
// itself (POST /planning/learning-goals, Content gives Planning no orders, rule 4) and hands its id to POST /demo, which
// pastes the demo material into it. The worker analyzes it like any document, from the answers that ship with it, no
// AI is asked. One demo per user: POST with a demo there gives it back. DELETE takes the demo documents with everything
// made of them, the learning goal stays with Planning until it can be deleted (#519). POST /demo/reset does both in the
// goal the demo has.
internal static class DemoEndpoints
{
    public const string NotAllowed = "Das Demo-Lernziel ist für dieses Konto nicht freigeschaltet.";

    public const string Unavailable = "Das Demo-Lernziel ließ sich gerade nicht anlegen. Versuch es gleich noch einmal.";

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/demo");

        demo.MapGet("/", async (IDemo demo, CancellationToken cancellationToken) =>
            await demo.FindAsync(cancellationToken) is { } found ? Results.Ok(DemoResponse.From(found)) : Results.NotFound());

        demo.MapPost("/", async (
            DemoRequest request,
            IDemo demo,
            ILearningGoals goals,
            ITestDoubleCaller caller,
            ICurrentUser user,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (!MayUse(caller, user, configuration))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: NotAllowed);
            }

            if (await demo.FindAsync(cancellationToken) is { } existing)
            {
                return Results.Ok(DemoResponse.From(existing));
            }

            if (request.LearningGoalId is not { } id)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["learningGoalId"] = ["Es fehlt das Lernziel."] });
            }

            try
            {
                if (await goals.FindAsync(id, cancellationToken) is not { } goal)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: FileUploadEndpoint.GoalNotFound);
                }

                var (created, made) = await demo.CreateAsync(goal, cancellationToken);
                return created ? Results.Created("/demo", DemoResponse.From(made)) : Results.Ok(DemoResponse.From(made));
            }
            catch (LearningGoalUnavailableException)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Unavailable);
            }
        });

        demo.MapDelete("/", async (
            IDemo demo, ITestDoubleCaller caller, ICurrentUser user, IConfiguration configuration, CancellationToken cancellationToken) =>
            !MayUse(caller, user, configuration) ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: NotAllowed)
            : await demo.DeleteAsync(cancellationToken) ? Results.NoContent()
            : Results.NotFound());

        // #412: the same goal again, so the plan Planning keeps for it starts over with the new topics.
        demo.MapPost("/reset", async (
            IDemo demo,
            ILearningGoals goals,
            ITestDoubleCaller caller,
            ICurrentUser user,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (!MayUse(caller, user, configuration))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: NotAllowed);
            }

            if (await demo.FindAsync(cancellationToken) is not { } existing)
            {
                return Results.NotFound();
            }

            try
            {
                if (await goals.FindAsync(existing.LearningGoalId, cancellationToken) is not { } goal)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: FileUploadEndpoint.GoalNotFound);
                }

                return Results.Ok(DemoResponse.From(await demo.ResetAsync(goal, cancellationToken)));
            }
            catch (LearningGoalUnavailableException)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Unavailable);
            }
        });

        return app;
    }

    // Until accounts get the right to it as a setting of their own: test accounts and the accounts the operator names
    // under Demo:Accounts (#414). For creating and for resetting alike.
    private static bool MayUse(ITestDoubleCaller caller, ICurrentUser user, IConfiguration configuration) =>
        caller.UsesTestDouble || (configuration.GetSection("Demo:Accounts").Get<string[]>() ?? []).Contains(user.Id, StringComparer.Ordinal);
}

internal sealed record DemoRequest(Guid? LearningGoalId);

// The goal of the demo and its documents in the order of the material, as GET /documents/{id} has them.
internal sealed record DemoResponse(Guid LearningGoalId, IReadOnlyList<DocumentResponse> Documents)
{
    public static DemoResponse From(DemoState demo) => new(demo.LearningGoalId, [.. demo.Documents.Select(DocumentResponse.From)]);
}