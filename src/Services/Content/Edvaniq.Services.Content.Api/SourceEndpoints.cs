using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.Net.Http.Headers;

namespace Edvaniq.Services.Content.Api;

// Opening a source in the original (#350): where a section comes from, and the uploaded file itself. Both only for the
// owner, someone else's looks like one that does not exist.
internal static class SourceEndpoints
{
    // The file comes from a user, so nothing in it may run or load anything, not even inside the browser's PDF viewer.
    // No sandbox: Chrome refuses to show a PDF in a sandboxed document.
    public const string FilePolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'none'";

    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sections/{sectionId:guid}/source", async (Guid sectionId, IDocuments documents, CancellationToken cancellationToken) =>
            await documents.SourceAsync(sectionId, cancellationToken) is { } source
                ? Results.Ok(SectionSourceResponse.From(source))
                : Results.NotFound());

        // The bytes as they were uploaded. A range of a foreign file is a 404 like the whole file, the owner check comes
        // first.
        app.MapGet("/documents/{id:guid}/file", async (Guid id, HttpContext context, IDocuments documents, IFiles files, CancellationToken cancellationToken) =>
        {
            if (await documents.FindAsync(id, cancellationToken) is not { FileId: { } fileId, FileFormat: { } format } document
                || await files.OpenAsync(fileId, cancellationToken) is not { } content)
            {
                return Results.NotFound();
            }

            var headers = context.Response.Headers;
            headers.ContentDisposition = DispositionOf(document.Title, format);
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = FilePolicy;
            headers.CacheControl = "no-store";
            return Results.Stream(content, ContentTypeOf(format), enableRangeProcessing: true);
        });

        return app;
    }

    // The title is the user's, so it goes into the header only through the framework, which quotes and encodes it. A
    // Word file can't be shown by the browser, it is saved instead.
    internal static string DispositionOf(string title, FileFormat format)
    {
        var disposition = new ContentDispositionHeaderValue(format == FileFormat.Docx ? "attachment" : "inline");
        disposition.SetHttpFileName(title + ExtensionOf(format));
        return disposition.ToString();
    }

    // Markdown goes out as plain text too: shown as it is, never rendered.
    internal static string ContentTypeOf(FileFormat format) => format switch
    {
        FileFormat.Pdf => "application/pdf",
        FileFormat.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "text/plain; charset=utf-8",
    };

    private static string ExtensionOf(FileFormat format) => format switch
    {
        FileFormat.Pdf => ".pdf",
        FileFormat.Docx => ".docx",
        FileFormat.Markdown => ".md",
        _ => ".txt",
    };
}

// Position counts the sections of the document from 0, as GET /documents/{id}/sections does. Format is pdf, docx, text
// or markdown, null for a pasted text. A PDF names its pages and has no text, any other document has the window around
// the section (SourceWindow): start and length count from the start of text.
internal sealed record SectionSourceResponse(
    Guid DocumentId,
    Guid SectionId,
    int Position,
    string Title,
    string? Format,
    bool HasFile,
    int? Page,
    int? PageTo,
    string? Text,
    int? Start,
    int? Length,
    bool TruncatedBefore,
    bool TruncatedAfter)
{
    public static SectionSourceResponse From(SectionSource source) =>
        new(
            source.DocumentId,
            source.SectionId,
            source.Position,
            source.Title,
            source.Format?.ToString().ToLowerInvariant(),
            source.HasFile,
            source.PageFrom,
            source.PageTo,
            source.Window?.Text,
            source.Window?.Start,
            source.Window?.Length,
            source.Window?.TruncatedBefore ?? false,
            source.Window?.TruncatedAfter ?? false);
}