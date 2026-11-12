namespace Edvaniq.Services.Content.Domain;

// Why a document failed, from a fixed list (#283). The code is for clients and statistics, the text is what the user
// reads. Every failure goes through one of the reasons in DocumentFailures, so none falls outside the list.
public sealed record DocumentFailure(FailureCode Code, string Text);

// The fixed list. The API sends the name.
public enum FailureCode
{
    // The file can't be read: broken, protected, packed too tightly, or it takes too long.
    Unreadable,

    // There's nothing to learn from in it.
    NoText,

    // The file or its text is larger than one document may be.
    TooLarge,

    // A limit of the account or of the app is used up for now.
    LimitReached,

    // The AI is down or paused.
    AiUnavailable,

    // The content isn't fit to learn from here.
    Inappropriate,

    // Anything else. The text says what to do.
    Unknown,
}

// Every reason a document can fail with. A new one goes here, with its code.
public static class DocumentFailures
{
    public static readonly DocumentFailure NothingToLearn = new(FailureCode.NoText, Document.NothingToLearn);

    public static readonly DocumentFailure NoTextInFile = new(FailureCode.NoText, Document.NoTextInFile);

    public static readonly DocumentFailure FileUnreadable = new(FailureCode.Unreadable, Document.FileUnreadable);

    public static readonly DocumentFailure FileNotProcessable = new(FailureCode.Unreadable, Document.FileNotProcessable);

    public static readonly DocumentFailure PdfProtected = new(FailureCode.Unreadable, FileCheck.PdfProtected);

    public static readonly DocumentFailure PackedTooTightly = new(FailureCode.Unreadable, FileCheck.PackedTooTightly);

    public static readonly DocumentFailure TakesTooLong = new(FailureCode.Unreadable, FileCheck.TakesTooLong);

    public static readonly DocumentFailure FileTextTooLong = new(FailureCode.TooLarge, Document.FileTextTooLong);

    public static readonly DocumentFailure TooManyPages = new(FailureCode.TooLarge, Document.TooManyPages);

    public static readonly DocumentFailure AiBudgetReached = new(FailureCode.LimitReached, Document.AiBudgetReached);

    public static readonly DocumentFailure AiUnavailable = new(FailureCode.AiUnavailable, Document.AiUnavailable);

    public static readonly DocumentFailure AiPaused = new(FailureCode.AiUnavailable, Document.AiPaused);

    public static readonly DocumentFailure Inappropriate = new(
        FailureCode.Inappropriate, "Der Inhalt eignet sich hier nicht zum Lernen. Lade anderes Material hoch.");

    public static readonly DocumentFailure SplitFailed = new(FailureCode.Unknown, Document.SplitFailed);

    public static readonly DocumentFailure AnalysisFailed = new(FailureCode.Unknown, Document.AnalysisFailed);

    public static readonly DocumentFailure QuestionsFailed = new(FailureCode.Unknown, Document.QuestionsFailed);

    public static readonly DocumentFailure TopicsMissing = new(FailureCode.Unknown, Document.TopicsMissing);

    public static readonly DocumentFailure GoalUnavailable = new(FailureCode.Unknown, Document.GoalUnavailable);

    public static readonly DocumentFailure GoalGone = new(FailureCode.Unknown, Document.GoalGone);

    public static readonly DocumentFailure AnalysisTimedOut = new(FailureCode.Unknown, Document.AnalysisTimedOut);

    public static readonly DocumentFailure Unknown = new(
        FailureCode.Unknown, "Etwas ist schiefgegangen. Starte die Analyse neu, und wenn es wieder scheitert, lade das Material noch einmal hoch.");

    public static IReadOnlyList<DocumentFailure> All { get; } =
    [
        NothingToLearn, NoTextInFile, FileUnreadable, FileNotProcessable, PdfProtected, PackedTooTightly, TakesTooLong,
        FileTextTooLong, TooManyPages, AiBudgetReached, AiUnavailable, AiPaused, Inappropriate, SplitFailed, AnalysisFailed,
        QuestionsFailed, TopicsMissing, GoalUnavailable, GoalGone, AnalysisTimedOut, Unknown,
    ];

    // The daily quotas name numbers and a time, so their texts are built from a template.
    public static IReadOnlyList<(string Template, FailureCode Code)> Templates { get; } =
    [
        (PageQuota.TooManyPagesReason, FailureCode.LimitReached),
        (GenerationQuota.TooManyReason, FailureCode.LimitReached),
    ];

    public static DocumentFailure PagesOverQuota(int pages, int left, DateTime now) =>
        new(FailureCode.LimitReached, PageQuota.TooManyPages(pages, left, now));

    public static DocumentFailure GenerationsOverQuota(int topics, int left, DateTime now) =>
        new(FailureCode.LimitReached, GenerationQuota.TooMany(topics, left, now));

    // The code of a stored text. A text that is no longer on the list counts as Unknown.
    public static FailureCode CodeOf(string text) =>
        All.FirstOrDefault(failure => failure.Text == text)?.Code
        ?? Templates.Where(template => text.StartsWith(FixedStartOf(template.Template), StringComparison.Ordinal))
            .Select(template => (FailureCode?)template.Code)
            .FirstOrDefault()
        ?? FailureCode.Unknown;

    // The part of a template before its first placeholder.
    public static string FixedStartOf(string template) => template[..template.IndexOf('{', StringComparison.Ordinal)];
}