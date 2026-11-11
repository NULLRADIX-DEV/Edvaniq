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

    public const string AnalysisFailed = "Die Themen ließen sich gerade nicht erkennen. Versuch es später noch einmal.";

    public const string QuestionsFailed = "Zu den Themen ließen sich gerade keine Fragen erstellen. Versuch es später noch einmal.";

    public const string TopicsMissing = "Die Themen kamen nicht an. Versuch es später noch einmal.";

    private Document(string ownerId, Guid? learningGoalId, string title, string text, DateTime createdAt)
    {
        OwnerId = ownerId;
        LearningGoalId = learningGoalId;
        Title = title;
        Text = text;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    // Planning's learning goal the text was pasted into, only a reference. Null for a document from before the default
    // goal (#215).
    public Guid? LearningGoalId { get; private set; }

    public string Title { get; private set; }

    public string Text { get; private set; }

    public DocumentStatus Status { get; private set; } = DocumentStatus.Uploaded;

    // Why the document failed, for the user. Null unless the status is Failed.
    public string? FailureReason { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // When the topics went out with DocumentAnalyzed, UTC. The document stays in analysis until its questions are there
    // too, null means the topics are still to come.
    public DateTime? AnalyzedAt { get; private set; }

    // When the user last started the failed analysis again, UTC. Null if they never did.
    public DateTime? RetriedAt { get; private set; }

    // The topics are reported, the question pool is what's left before the document is ready.
    public bool WaitsForQuestions => Status == DocumentStatus.Analyzing && AnalyzedAt is not null;

    // Since when the document waits for Knowledge's topics: the analysis, or a retry after it.
    public DateTime? WaitsForTopicsSince => RetriedAt > AnalyzedAt ? RetriedAt : AnalyzedAt;

    // A word counts from this many letters in a row, so page numbers and stray marks from a scan don't.
    public const int WordMinLetters = 3;

    // Below this many words there's nothing the analysis could learn from (docs/spec/acceptance.md, MVP4).
    public const int MinWords = 8;

    public const string TooFewWords =
        "Der Text enthält zu wenig lesbaren Inhalt, etwa weil er aus einem Scan stammt. Füge einen Text mit ganzen Sätzen ein, zum Beispiel aus einer Textversion des Materials.";

    // Why the text can't become a document, or null if it can. The client shows the message as it is.
    public static string? CheckText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "Der Text ist leer. Füge den Text ein, aus dem du lernen willst."
        : text.Length > TextMaxLength ? "Der Text ist zu lang. Erlaubt sind höchstens 200.000 Zeichen."
        : !HasEnoughWords(text) ? TooFewWords
        : null;

    // Counts runs of letters, digits and marks end a run. Stops as soon as there are enough.
    public static bool HasEnoughWords(string text)
    {
        var words = 0;
        var run = 0;
        foreach (var c in text)
        {
            if (char.IsLetter(c))
            {
                run++;
                continue;
            }

            if (run >= WordMinLetters && ++words >= MinWords)
            {
                return true;
            }

            run = 0;
        }

        return run >= WordMinLetters && words + 1 >= MinWords;
    }

    // Why the title can't stay, or null if it can. No title is fine, Paste takes one from the text.
    public static string? CheckTitle(string? title) =>
        title?.Trim().Length > TitleMaxLength ? "Der Titel ist zu lang. Erlaubt sind höchstens 80 Zeichen." : null;

    // A new document in status Uploaded. Call it only with a text and a title that passed the checks.
    public static Document Paste(string ownerId, Guid learningGoalId, string? title, string text, DateTime createdAt) =>
        new(ownerId, learningGoalId, string.IsNullOrWhiteSpace(title) ? FirstLine(text) : title.Trim(), text, createdAt);

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

    // The topics are reported. Knowledge gives them their ids, then the questions follow. Only once per document.
    public void ReportTopics(DateTime analyzedAt)
    {
        if (Status != DocumentStatus.Analyzing || AnalyzedAt is not null)
        {
            throw new InvalidOperationException($"Document {Id} is {Status}, only a document in analysis can report its topics once.");
        }

        AnalyzedAt = analyzedAt;
    }

    // Topics and questions are there, the document is ready to learn from. Only a document whose topics are reported gets
    // there.
    public void FinishAnalysis()
    {
        if (!WaitsForQuestions)
        {
            throw new InvalidOperationException($"Document {Id} is {Status}, only a document waiting for its questions can finish.");
        }

        Status = DocumentStatus.Ready;
    }

    // The reason goes to the user as it is, so it says what happened and what they can do.
    public void Fail(string reason)
    {
        Status = DocumentStatus.Failed;
        FailureReason = reason[..Math.Min(reason.Length, FailureReasonMaxLength)];
    }

    // Starts a failed analysis again where it failed. Before the topics went out it starts over from splitting: no topic
    // points at a section yet, and splitting costs nothing. After that the topics stay as Knowledge has them and only the
    // questions follow, so DocumentAnalyzed never goes out twice. Only a failed document can be retried.
    public void Retry(DateTime retriedAt)
    {
        if (Status != DocumentStatus.Failed)
        {
            throw new InvalidOperationException($"Document {Id} is {Status}, only a failed document can be retried.");
        }

        Status = AnalyzedAt is null ? DocumentStatus.Uploaded : DocumentStatus.Analyzing;
        FailureReason = null;
        RetriedAt = retriedAt;
    }

    // The first line with content, cut to a title. Trim takes the '\r' of Windows line ends along.
    private static string FirstLine(string text)
    {
        var first = text.Split('\n').Select(line => line.Trim()).First(line => line.Length > 0);
        return first[..Math.Min(first.Length, TitleMaxLength)].TrimEnd();
    }
}