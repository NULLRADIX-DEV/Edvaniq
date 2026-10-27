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

        documents.MapPost("/", async (PastedText request, IDocuments documents, CancellationToken cancellationToken) =>
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

            var document = await documents.PasteAsync(request.Title, request.Text!, cancellationToken);
            return Results.Created($"/documents/{document.Id}", DocumentResponse.From(document));
        });

        documents.MapGet("/", async (IDocuments documents, CancellationToken cancellationToken) =>
            (await documents.ListAsync(cancellationToken)).Select(DocumentResponse.From));

        // Someone else's document and one that does not exist look the same: 404.
        documents.MapGet("/{id:guid}", async (Guid id, IDocuments documents, CancellationToken cancellationToken) =>
            await documents.FindAsync(id, cancellationToken) is { } document
                ? Results.Ok(DocumentResponse.From(document))
                : Results.NotFound());

        return app;
    }
}

internal sealed record PastedText(string? Title, string? Text);

// Status goes out by name (Uploaded, Analyzing, Ready, Failed), the client words it.
internal sealed record DocumentResponse(Guid Id, string Title, string Status, DateTime CreatedAt)
{
    public static DocumentResponse From(Document document) =>
        new(document.Id, document.Title, document.Status.ToString(), document.CreatedAt);

    public static DocumentResponse From(DocumentSummary document) =>
        new(document.Id, document.Title, document.Status.ToString(), document.CreatedAt);
}
