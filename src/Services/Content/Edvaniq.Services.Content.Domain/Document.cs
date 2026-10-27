using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A text the user pasted into their material (docs/spec/glossary.md#dokument). The worker picks up every uploaded
// document from the database, so storing it is what starts the analysis. The text stays exactly as pasted, sections
// point into it.
public sealed class Document : IOwnedByUser
{
    public const int TextMaxLength = 200_000;

    public const int TitleMaxLength = 80;

    public const int FailureReasonMaxLength = 200;

    // The reasons the client shows for a failed document.
    public const string NothingToLearn = "Der Text enthält nichts, woraus sich lernen lässt. Füge einen anderen Text ein.";

    public const string SplitFailed = "Der Text ließ sich nicht in Abschnitte zerlegen. Füge ihn noch einmal ein.";

    public const string AnalysisFailed = "Die Themen ließen sich gerade nicht erkennen. Füge den Text später noch einmal ein.";

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

    // Why the document failed, for the user. Null unless the status is Failed.
    public string? FailureReason { get; private set; }

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

    // Cuts the text into sections and moves the document on to the analysis. A text with nothing but white space fails
    // instead and has no sections. Only an uploaded document can be split.
    public IReadOnlyList<Section> Split()
    {
        if (Status != DocumentStatus.Uploaded)
        {
            throw new InvalidOperationException($"Document {Id} is {Status}, only an uploaded document can be split.");
        }

        var sections = SectionSplitter.Split(Text).Select((span, ordinal) => Section.Of(this, ordinal, span)).ToList();
        if (sections.Count == 0)
        {
            Fail(NothingToLearn);
        }
        else
        {
            Status = DocumentStatus.Analyzing;
        }

        return sections;
    }

    // The topics are known and reported, the document is ready to learn from. Only a document in analysis gets there.
    public void FinishAnalysis()
    {
        if (Status != DocumentStatus.Analyzing)
        {
            throw new InvalidOperationException($"Document {Id} is {Status}, only a document in analysis can finish it.");
        }

        Status = DocumentStatus.Ready;
    }

    // The reason goes to the user as it is, so it says what happened and what they can do.
    public void Fail(string reason)
    {
        Status = DocumentStatus.Failed;
        FailureReason = reason[..Math.Min(reason.Length, FailureReasonMaxLength)];
    }

    // The first line with content, cut to a title. Trim takes the '\r' of Windows line ends along.
    private static string FirstLine(string text)
    {
        var first = text.Split('\n').Select(line => line.Trim()).First(line => line.Length > 0);
        return first[..Math.Min(first.Length, TitleMaxLength)].TrimEnd();
    }
}