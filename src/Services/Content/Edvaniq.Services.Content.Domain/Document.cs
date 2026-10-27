using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A text the user pasted into their material (docs/spec/glossary.md#dokument). The worker picks up every uploaded
// document from the database, so storing it is what starts the analysis. The text stays exactly as pasted, sections
// will point into it.
public sealed class Document : IOwnedByUser
{
    public const int TextMaxLength = 200_000;

    public const int TitleMaxLength = 80;

    private Document(string ownerId, string title, string text, DateTime createdAt)
    {
        OwnerId = ownerId;
        Title = title;
        Text = text;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    public string Title { get; private set; }

    public string Text { get; private set; }

    public DocumentStatus Status { get; private set; } = DocumentStatus.Uploaded;

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // Why the text can't become a document, or null if it can. The client shows the message as it is.
    public static string? CheckText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "Der Text ist leer. Füge den Text ein, aus dem du lernen willst."
        : text.Length > TextMaxLength ? "Der Text ist zu lang. Erlaubt sind höchstens 200.000 Zeichen."
        : null;

    // Why the title can't stay, or null if it can. No title is fine, Paste takes one from the text.
    public static string? CheckTitle(string? title) =>
        title?.Trim().Length > TitleMaxLength ? "Der Titel ist zu lang. Erlaubt sind höchstens 80 Zeichen." : null;

    // A new document in status Uploaded. Call it only with a text and a title that passed the checks.
    public static Document Paste(string ownerId, string? title, string text, DateTime createdAt) =>
        new(ownerId, string.IsNullOrWhiteSpace(title) ? FirstLine(text) : title.Trim(), text, createdAt);

    // The first line with content, cut to a title. Trim takes the '\r' of Windows line ends along.
    private static string FirstLine(string text)
    {
        var first = text.Split('\n').Select(line => line.Trim()).First(line => line.Length > 0);
        return first[..Math.Min(first.Length, TitleMaxLength)].TrimEnd();
    }
}