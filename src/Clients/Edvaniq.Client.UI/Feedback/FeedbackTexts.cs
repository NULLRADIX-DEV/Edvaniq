namespace Edvaniq.Client.UI.Feedback;

// Every feedback text of the app, in one place so the rules in docs/ux-texts.md can be checked against all of them.
// Text from the AI (questions, options, explanations) never goes through here.
public static class FeedbackTexts
{
    public static FeedbackText For(SessionResult result) => result switch
    {
        SessionResult.VeryGood => new(
            "Stark, fast alles richtig.",
            "Du hast dieses Thema gut im Griff. Die Wiederholung sorgt dafür, dass es so bleibt."),
        SessionResult.Good => new(
            "Gut gelaufen.",
            "Das meiste sitzt schon. Was noch offen ist, kommt in der Wiederholung wieder dran."),
        SessionResult.Mixed => new(
            "Ein guter Schritt nach vorn.",
            "Einiges sitzt schon, anderes ist noch im Aufbau. Mit den Erklärungen von heute geht es beim nächsten Mal leichter."),
        SessionResult.ManyMistakes => new(
            "Gut, dass du drangeblieben bist.",
            "Bei diesem Thema ist noch vieles neu, und genau dafür ist das Üben da. Die Fragen kommen wieder, und mit den Erklärungen von heute fällt es dir dann leichter."),
        SessionResult.Cancelled => new(
            "Pause gemacht.",
            "Deine Antworten bis hierhin zählen. Wenn du weitermachst, geht es mit den übrigen Fragen weiter."),
        SessionResult.NoQuestions => new(
            "Für diese Session gibt es gerade keine Fragen.",
            "Fragen entstehen nach dem Einfügen eines Textes im Hintergrund. Schau später noch einmal vorbei oder wähl ein anderes Thema."),
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };

    // Right after one answer in practice.
    public static class Answer
    {
        public const string Right = "Richtig! Gut gemacht.";

        public const string Wrong = "Diesmal nicht richtig.";

        public const string WrongHint = "Mit der Erklärung sitzt es beim nächsten Mal.";

        // Nobody answered, so it's no verdict, even though it counts as wrong.
        public const string Revealed = "Hier ist die Lösung.";

        public const string RevealedHint = "Schau dir die Erklärung in Ruhe an, so bleibt sie hängen.";
    }

    // Hints on the practice page around the question.
    public static class Quiz
    {
        public const string Loading = "Wird geladen …";

        public const string NoQuestions = "Zu diesem Thema gibt es noch keine Fragen.";

        public const string NoQuestionsHint =
            "Sie entstehen nach dem Einfügen im Hintergrund und brauchen meist ein paar Minuten. Versuch es gleich noch einmal oder wähl ein anderes Thema.";

        public const string TopicGone = "Dieses Thema gibt es nicht oder nicht mehr.";

        public const string LoadFailed = "Die Frage ließ sich gerade nicht laden.";

        public const string NotThrough = "Deine Antwort ist nicht angekommen.";

        public const string NotThroughHint = "Schickst du sie noch einmal, zählt sie trotzdem nur einmal.";

        public const string NothingChosen = "Wähl zuerst eine Antwort aus.";
    }

    // When the login ran out or ended elsewhere. What was not sent yet stays (Shell/Drafts).
    public static class Session
    {
        public const string Ended = "Deine Anmeldung ist abgelaufen.";

        public const string EndedHint = "Melde dich neu an, dann geht es hier weiter. Was du noch nicht gesendet hast, bleibt erhalten.";

        public const string SignInAgain = "Neu anmelden";

        public const string OnLogin = "Nach dem Anmelden geht es dort weiter, wo du warst.";

        public const string TextKept = "Dein Text ist noch nicht angekommen. Er bleibt hier, bis du dich neu angemeldet hast.";

        public const string TextBack = "Dein Text von vorhin ist noch da. Schick ihn ab, wenn er fertig ist.";

        public const string AnswerKept = "Deine Antwort ist noch nicht angekommen. Nach dem Anmelden kannst du sie hier noch einmal prüfen, sie zählt einmal.";

        public const string AnswerBack = "Deine Frage von vorhin ist noch offen. Prüf deine Antwort, sie zählt einmal.";
    }

    // Starting a failed analysis again on the topics page.
    public static class Retry
    {
        public const string Button = "Erneut analysieren";

        public const string Starting = "Wird gestartet …";

        public const string NotThrough = "Der neue Versuch ließ sich gerade nicht starten.";
    }

    // Uploading a file on the topics page. The messages for a refused file match Content's words.
    public static class Upload
    {
        public const string Heading = "Datei hochladen";

        public const string Label = "Datei auswählen";

        public const string Hint = "Dateien mit .pdf, .docx, .txt oder .md, höchstens 20 MB.";

        public const string Button = "Hochladen";

        public const string Sending = "Wird hochgeladen …";

        public const string NothingChosen = "Wähl zuerst eine Datei aus.";

        // Same words as the web host's UploadLimit.TooLarge and Content's FileCheck.TooLarge.
        public const string TooLarge = "Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.";

        public const string Unsupported =
            "Dieser Dateityp wird nicht unterstützt. Lade eine Datei mit .pdf, .docx, .txt oder .md hoch.";

        public const string NotThrough = "Die Datei ist nicht angekommen.";
    }

    // When a call or the app itself didn't get through. Never the user's fault.
    public static class Connection
    {
        public const string Advice =
            "Prüf deine Internetverbindung. Steht sie, liegt es an uns: Versuch es in ein paar Sekunden noch einmal.";

        // Around a plain link while the browser app loads: "… und <a>lade die Seite neu</a>."
        public const string LoadingStuck = "Bleibt das so, prüf deine Internetverbindung und";

        public const string LoadingStuckLink = "lade die Seite neu";
    }
}

public sealed record FeedbackText(string Title, string Body);

// How a session ended. Which numbers lead to which state is up to the result data, not to the texts.
public enum SessionResult
{
    VeryGood,
    Good,
    Mixed,
    ManyMistakes,
    Cancelled,
    NoQuestions,
}