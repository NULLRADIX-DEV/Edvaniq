using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Api;

// Pasting a text and seeing where its documents stand. The owner never appears in a request or a response: it comes from
// the token, and the owner filter applies it.
internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var documents = app.MapGroup("/documents");
        documents.MapFileUpload();

        documents.MapPost("/", async (PastedText request, ILearningGoals goal, IDocuments documents, IAiBudget budget, CancellationToken cancellationToken) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (Document.CheckText(request.Text) is { } text)
            {
                errors[nameof(request.Text)] = [text];
            }

            if (Document.CheckTitle(request.Title) is { } title)
            {
                errors[nameof(request.Title)] = [title];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            if (await BudgetRefusal.OfAsync(budget, cancellationToken) is { } refused)
            {
                return refused;
            }

            // The goal before the save: without Planning nothing is stored, the user just sends the text again. Without
            // learningGoalId the default goal. Someone else's goal gets the same answer as one that doesn't exist.
            LearningGoalInfo learningGoal;
            try
            {
                if (request.LearningGoalId is not { } id)
                {
                    learningGoal = await goal.GetDefaultAsync(cancellationToken);
                }
                else if (await goal.FindAsync(id, cancellationToken) is { } found)
                {
                    learningGoal = found;
                }
                else
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: FileUploadEndpoint.GoalNotFound);
                }
            }
            catch (LearningGoalUnavailableException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Der Text ließ sich gerade nicht speichern. Versuch es gleich noch einmal.");
            }

            if (await GoalFullAsync(documents, learningGoal, cancellationToken) is { } full)
            {
                return full;
            }

            var document = await documents.PasteAsync(learningGoal, request.Title, request.Text!, cancellationToken);
            return Results.Created($"/documents/{document.Id}", DocumentResponse.From(document));
        });

        documents.MapGet("/", async (IDocuments documents, CancellationToken cancellationToken) =>
            (await documents.ListAsync(cancellationToken)).Select(DocumentResponse.From));

        // Someone else's document and one that does not exist look the same: 404.
        documents.MapGet("/{id:guid}", async (Guid id, IDocuments documents, CancellationToken cancellationToken) =>
            await documents.FindAsync(id, cancellationToken) is { } document
                ? Results.Ok(DocumentResponse.From(document))
                : Results.NotFound());

        // Starts the analysis of a failed document again, the worker picks it up like a new one. 202 with the document as
        // it is now, 409 if it isn't failed (any more). With the budget of the month used up, 429 for any id.
        documents.MapPost("/{id:guid}/retry", async (Guid id, IDocuments documents, IAiBudget budget, CancellationToken cancellationToken) =>
            await BudgetRefusal.OfAsync(budget, cancellationToken) ?? await documents.RetryAsync(id, cancellationToken) switch
            {
                (RetryResult.Started, { } document) => Results.Accepted($"/documents/{document.Id}", DocumentResponse.From(document)),
                (RetryResult.NotFound, _) => Results.NotFound(),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Die Analyse dieses Textes läuft schon oder ist fertig."),
            });

        // Deletes the document and everything Content made from it, the other services follow DocumentDeleted. 204, or
        // 404 for someone else's document and one that does not exist (any more).
        documents.MapDelete("/{id:guid}", async (Guid id, IDocuments documents, CancellationToken cancellationToken) =>
            await documents.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        return app;
    }

    // 409 with the message in title when the goal holds MaxDocumentsPerGoal already (#277), null if there is room.
    internal static async Task<IResult?> GoalFullAsync(IDocuments documents, LearningGoalInfo goal, CancellationToken cancellationToken) =>
        await documents.CountInGoalAsync(goal.Id, cancellationToken) >= Document.MaxDocumentsPerGoal
            ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Document.GoalFull)
            : null;
}

internal sealed record PastedText(string? Title, string? Text, Guid? LearningGoalId = null);

// Status goes out by name (Uploaded, Analyzing, Ready, Failed), the client words it. A failed document says why. Source
// says where it came from, Text or File: one from a file stays Uploaded until its text is extracted (#282).
internal sealed record DocumentResponse(Guid Id, string Title, string Status, string? FailureReason, DateTime CreatedAt, string Source)
{
    public static DocumentResponse From(Document document) =>
        new(document.Id, document.Title, document.Status.ToString(), document.FailureReason, document.CreatedAt, SourceOf(document.IsFromFile));

    public static DocumentResponse From(DocumentSummary document) =>
        new(document.Id, document.Title, document.Status.ToString(), document.FailureReason, document.CreatedAt, SourceOf(document.IsFromFile));

    private static string SourceOf(bool isFromFile) => isFromFile ? "File" : "Text";
}