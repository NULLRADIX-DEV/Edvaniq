namespace Edvaniq.Client.Core;

// Where a question or a summary comes from in the user's own material (#350), from Content through the web host.
public interface ISources
{
    // The section's place in its document. NotFound when the section or its document is gone, or isn't the user's.
    Task<CallOutcome<SectionSource>> SectionAsync(Guid sectionId, CancellationToken cancellationToken = default);
}

// Format is one of SourceFormats, null for a pasted text. A PDF names its pages (Page, PageTo) and has no text, any other
// document a window of text around the section: Start and Length mark the section in it, Truncated says text was left
// out before or after. Position counts the sections of the document from 0, if Content names it.
public sealed record SectionSource(
    Guid DocumentId,
    Guid SectionId,
    string Title,
    string? Format,
    bool HasFile,
    int? Page = null,
    int? PageTo = null,
    string? Text = null,
    int? Start = null,
    int? Length = null,
    bool TruncatedBefore = false,
    bool TruncatedAfter = false,
    int? Position = null);

public static class SourceFormats
{
    public const string Pdf = "pdf";
    public const string Docx = "docx";
    public const string Text = "text";
    public const string Markdown = "markdown";
}