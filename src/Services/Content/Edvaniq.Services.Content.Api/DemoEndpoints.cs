using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.Api;

// The demo learning goal (#411). The client asks GET /demo first. Without a demo it creates a learning goal in Planning
// itself (POST /planning/learning-goals, Content gives Planning no orders, rule 4) and hands its id to POST /demo, which
// pastes the demo material into it. The worker analyzes it like any document, from the answers that ship with it, no
// AI is asked. One demo per user: POST with a demo there gives it back. DELETE takes the demo documents with everything
// made of them, the learning goal stays with Planning until it can be deleted (#519).
internal static class DemoEndpoints
{
    public const string NotAllowed = "Das Demo-Lernziel ist für dieses Konto nicht freigeschaltet.";

    public const string Unavailable = "Das Demo-Lernziel ließ sich gerade nicht anlegen. Versuch es gleich noch einmal.";

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/demo");

        demo.MapGet("/", async (IDemo demo, CancellationToken cancellationToken) =>
            await demo.DocumentsAsync(cancellationToken) is { Count: > 0 } documents
                ? Results.Ok(DemoResponse.From(documents))
                : Results.NotFound());

        demo.MapPost("/", async (
            DemoRequest request,
            IDemo demo,
            ILearningGoals goals,
            ITestDoubleCaller caller,
            ICurrentUser user,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            // Until accounts get the right to it (#414): test accounts and the accounts the operator names.
            if (!caller.UsesTestDouble && !(configuration.GetSection("Demo:Accounts").Get<string[]>() ?? []).Contains(user.Id, StringComparer.Ordinal))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: NotAllowed);
            }

            if (await demo.DocumentsAsync(cancellationToken) is { Count: > 0 } existing)
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

                return Results.Created("/demo", DemoResponse.From(await demo.CreateAsync(goal, cancellationToken)));
            }
            catch (LearningGoalUnavailableException)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Unavailable);
            }
        });

        demo.MapDelete("/", async (IDemo demo, IDocuments documents, CancellationToken cancellationToken) =>
        {
            var demoDocuments = await demo.DocumentsAsync(cancellationToken);
            foreach (var document in demoDocuments)
            {
                await documents.DeleteAsync(document.Id, cancellationToken);
            }

            return demoDocuments.Count > 0 ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}

internal sealed record DemoRequest(Guid? LearningGoalId);

// The goal of the demo and its documents in the order of the material, as GET /documents/{id} has them.
internal sealed record DemoResponse(Guid LearningGoalId, IReadOnlyList<DocumentResponse> Documents)
{
    public static DemoResponse From(IReadOnlyList<Edvaniq.Services.Content.Domain.Document> documents) =>
        new(documents[0].LearningGoalId!.Value, [.. documents.Select(DocumentResponse.From)]);
}