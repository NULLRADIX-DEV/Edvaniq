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

    public const int LevelMaxLength = 32;

    public const int OutputLanguageMaxLength = 8;

    // The reasons the client shows for a failed document.
    public const string NothingToLearn = "Der Text enthält nichts, woraus sich lernen lässt. Füge einen anderen Text ein.";

    public const string SplitFailed = "Der Text ließ sich nicht in Abschnitte zerlegen. Füge ihn noch einmal ein.";

    public const string AnalysisFailed = "Die Themen ließen sich gerade nicht erkennen. Versuch es später noch einmal.";

    public const string QuestionsFailed = "Zu den Themen ließen sich gerade keine Fragen erstellen. Versuch es später noch einmal.";

    public const string TopicsMissing = "Die Themen kamen nicht an. Versuch es später noch einmal.";

    public const string AiUnavailable = "KI derzeit nicht verfügbar. Versuch es später noch einmal.";

    // Planning couldn't say level and output language of the goal. Never guessed, a retry asks again.
    public const string GoalUnavailable = "Das Lernziel ließ sich gerade nicht abrufen. Versuch es später noch einmal.";

    public const string GoalGone = "Das Lernziel dieses Dokuments gibt es nicht mehr.";

    // The AI cost this month reached the operator's budget (AiSwitch.MonthlyBudget). Names no amount.
    public const string AiBudgetReached = "Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.";

    // The operator paused the AI (AiSwitch). A retry works once it runs again.
    public const string AiPaused = "KI derzeit pausiert. Versuch es später noch einmal.";

    public const string FileUnreadable = "Die Datei ließ sich nicht lesen. Lade sie noch einmal hoch, zum Beispiel neu gespeichert.";

    public const string FileTextTooLong = "Die Datei enthält zu viel Text. Erlaubt sind höchstens 200.000 Zeichen, teile sie auf.";

    public const string TooManyPages = "Die Datei hat zu viele Seiten. Erlaubt sind höchstens 300, teile sie auf.";

    // The worker broke off on the file more than once, it went down while reading it (#318).
    public const string FileNotProcessable =
        "Die Datei ließ sich nicht verarbeiten. Speichere sie neu oder teile sie in kleinere Dateien auf und lade sie noch einmal hoch.";

    // How often reading a file may break off before the document fails. One more try covers a worker that was killed for
    // another reason, a file that brings it down every time stops after the second.
    public const int MaxUnfinishedReads = 2;

    // A PDF beyond this many pages is turned away before its pages are read. 300 is more than a school book chapter or a
    // script needs, and keeps reading one file within seconds and within the worker's memory.
    public const int MaxPages = 300;

    // How long a document waits after each outage of the AI before the next attempt. After the last one it fails.
    public static readonly IReadOnlyList<TimeSpan> OutageDelays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(8)];

    // The longest wait a provider's Retry-After may ask for, so one answer can't park a document for hours.
    public static readonly TimeSpan MaxOutageDelay = TimeSpan.FromMinutes(15);

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

    // No longer written or read since #323: the goal keeps level and output language, Content keeps no copy. The columns
    // stay so a rollback to a version that writes them still works, they go with a later migration.
    public string? Level { get; private set; }

    public string? OutputLanguage { get; private set; }

    public string Title { get; private set; }

    // The pasted text. Empty for a document from a file until its text is extracted (#282).
    public string Text { get; private set; }

    // The uploaded file the document comes from, null for a pasted text.
    public Guid? FileId { get; private set; }

    public FileFormat? FileFormat { get; private set; }

    public bool IsFromFile => FileId is not null;

    // When the worker read the text out of the file, UTC. Null for a pasted text and while the file waits for it.
    public DateTime? TextExtractedAt { get; private set; }

    // How often the worker started reading the file and neither finished nor stopped on purpose, because it went down
    // meanwhile. Back to 0 once the reading ends one way or another.
    public int UnfinishedReads { get; private set; }

    // A document from a file that the worker still has to read.
    public bool WaitsForText => Status == DocumentStatus.Uploaded && IsFromFile && TextExtractedAt is null;

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

    // Outages of the AI in the current step of the analysis, each one postponed the document.
    public int AiOutages { get; private set; }

    // When the worker may ask the AI again after an outage, UTC. Null means right away.
    public DateTime? NextAttemptAt { get; private set; }

    // The worker asks the simulated AI instead of the real one. Only for documents of a team account the operator marked
    // for tests (docs/tokens.md), never from a field of the request.
    public bool UsesTestDouble { get; private set; }

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
    // Level and output language stay with the goal, the worker asks Planning for them before every AI step (#323).
    public static Document Paste(
        string ownerId,
        Guid learningGoalId,
        string? title,
        string text,
        DateTime createdAt,
        bool usesTestDouble = false) =>
        new(ownerId, learningGoalId, string.IsNullOrWhiteSpace(title) ? FirstLine(text) : title.Trim(), text, createdAt)
        {
            UsesTestDouble = usesTestDouble,
        };

    // A new document in status Uploaded from a stored file whose content passed FileCheck. The file's name only gives the
    // title, the text follows once it is extracted.
    public static Document Upload(
        string ownerId,
        Guid learningGoalId,
        string? fileName,
        Guid fileId,
        FileFormat format,
        DateTime createdAt,
        bool usesTestDouble = false) =>
        new(ownerId, learningGoalId, TitleOf(fileName), "", createdAt)
        {
            FileId = fileId,
            FileFormat = format,
            UsesTestDouble = usesTestDouble,
        };

    public const string UntitledFile = "Hochgeladene Datei";

    // The file's name without folders and extension, without control characters, cut to a title.
    public static string TitleOf(string? fileName)
    {
        var name = (fileName ?? "").Split('/', '\\')[^1];
        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            name = name[..dot];
        }

        name = new string([.. name.Where(c => !char.IsControl(c))]).Trim();
        return name.Length == 0 ? UntitledFile : name[..Math.Min(name.Length, TitleMaxLength)].TrimEnd();
    }

    // Takes the text the worker read out of the file, one entry per page from page 1, and returns where each page lies in
    // it. Pages are joined by a blank line, so a page always ends a paragraph. Too much text fails the document instead,
    // without pages. The document stays uploaded, the splitter takes it from here.
    public IReadOnlyList<DocumentPage> TakeText(IReadOnlyList<string> pages, DateTime extractedAt)
    {
        if (!WaitsForText)
        {
            throw new InvalidOperationException($"Document {Id} waits for no text.");
        }

        var text = new System.Text.StringBuilder();
        var spans = new List<DocumentPage>();
        foreach (var page in pages)
        {
            if (spans.Count > 0)
            {
                text.Append(PageBreak);
            }

            var content = page.Trim();
            spans.Add(DocumentPage.Of(this, spans.Count + 1, text.Length, content.Length));
            text.Append(content);
        }

        // Without TextExtractedAt a new attempt reads the file again.
        if (text.Length > TextMaxLength)
        {
            Fail(FileTextTooLong);
            return [];
        }

        Text = text.ToString();
        TextExtractedAt = extractedAt;
        UnfinishedReads = 0;
        return spans;
    }

    public const string PageBreak = "\n\n";

    // Notes that the worker starts reading the file, stored before it opens it. Returns false and fails the document if
    // reading broke off too often already, a file like that would take the worker down again.
    public bool StartReading()
    {
        if (!WaitsForText)
        {
            throw new InvalidOperationException($"Document {Id} waits for no text.");
        }

        if (UnfinishedReads >= MaxUnfinishedReads)
        {
            Fail(FileNotProcessable);
            return false;
        }

        UnfinishedReads++;
        return true;
    }

    // The worker stopped reading on purpose, a shutdown during a deploy. That attempt doesn't count.
    public void StopReading() => UnfinishedReads = Math.Max(0, UnfinishedReads - 1);

    // Cuts the text into sections and moves the document on to the analysis. A text with nothing but white space fails
    // instead and has no sections. Only an uploaded document can be split, and one from a file only once its text is
    // there (#282).
    public IReadOnlyList<Section> Split()
    {
        if (Status != DocumentStatus.Uploaded)
        {
            throw new InvalidOperationException($"Document {Id} is {Status}, only an uploaded document can be split.");
        }

        if (WaitsForText)
        {
            throw new InvalidOperationException($"Document {Id} comes from a file, its text isn't extracted yet.");
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
        ClearOutages();
    }

    // The AI was away for a moment. The document waits delays[n] for the n-th outage, or longer if the provider asked
    // for it (up to MaxOutageDelay), and keeps its status. Once the delays are used up it fails, the user can retry.
    // Returns false if it failed.
    public bool Postpone(DateTime now, IReadOnlyList<TimeSpan> delays, TimeSpan? retryAfter)
    {
        if (Status != DocumentStatus.Analyzing)
        {
            throw new InvalidOperationException($"Document {Id} is {Status}, only a document in analysis can wait for the AI.");
        }

        if (AiOutages >= delays.Count)
        {
            Fail(AiUnavailable);
            return false;
        }

        var delay = delays[AiOutages];
        if (retryAfter > delay)
        {
            delay = retryAfter < MaxOutageDelay ? retryAfter.Value : MaxOutageDelay;
        }

        AiOutages++;
        NextAttemptAt = now + delay;
        return true;
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
        UnfinishedReads = 0;
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
        ClearOutages();
        UnfinishedReads = 0;
    }

    private void ClearOutages()
    {
        AiOutages = 0;
        NextAttemptAt = null;
    }

    // The first line with content, cut to a title. Trim takes the '\r' of Windows line ends along.
    private static string FirstLine(string text)
    {
        var first = text.Split('\n').Select(line => line.Trim()).First(line => line.Length > 0);
        return first[..Math.Min(first.Length, TitleMaxLength)].TrimEnd();
    }
}