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

        public const string AskAgain = "Noch einmal versuchen";

        public const string OtherTopic = "Anderes Thema wählen";

        public const string BackToTopics = "Zurück zu den Themen";
    }

    // The newest text and the topics on the topics page.
    public static class Material
    {
        public const string LoadFailed = "Der Stand ließ sich gerade nicht laden.";

        public const string Empty = "Du hast noch keinen Text eingefügt.";

        public const string EmptyHint = "Füge unten deinen ersten Text ein, dann erscheinen hier deine Themen.";

        public const string KeepWaiting = "Weiter warten";

        public const string TextNotThrough =
            "Der Text ist nicht angekommen. Edvaniq ist gerade nicht erreichbar. Versuch es in ein paar Sekunden noch einmal.";

        // {0} is the title of the newest text, placed by the view.
        public const string Analyzing = "„{0}“ wird analysiert …";

        public const string AnalyzingHint = "Das kann ein paar Minuten dauern. Die Themen erscheinen hier von selbst.";

        public const string AnalysisSlow = "Die Analyse von „{0}“ dauert länger als gedacht.";

        public const string AnalysisSlowHint = "Du kannst weiter warten oder später wiederkommen.";

        public const string AnalysisFailed = "Die Analyse von „{0}“ hat nicht geklappt.";

        // When Content names no reason.
        public const string AnalysisFailedHint = "Versuch es noch einmal.";

        public const string Ready = "Die Themen aus „{0}“ sind da.";

        public static string WithTitle(string text, string title) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, text, title);
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

    // The mastery after an answer, which comes a moment later (#222).
    public static class Progress
    {
        public const string Heading = "Dein Lernstand";

        public const string Updating = "Dein Lernstand wird aktualisiert …";

        public const string Late = "Dein Lernstand ist noch nicht aktualisiert.";

        public const string LateHint = "Deine Antwort ist gespeichert und zählt. Prüf es gleich noch einmal.";

        public const string Check = "Erneut prüfen";

        public const string New = "neu";

        public const string Weakness = "Schwäche";

        public const string Building = "im Aufbau";

        public const string Secure = "sicher";
    }

    // Today's plan on Home.
    public static class Plan
    {
        public const string Updating = "Dein Plan wird gerade aktualisiert …";

        public const string Late = "Dein Plan ist noch nicht aktualisiert.";

        public const string LateHint = "Deine Antworten sind gespeichert. Lade den Plan gleich noch einmal.";

        public const string Learn = "Lernen";

        public const string Review = "Wiederholen";

        public const string Minute = "Minute";

        public const string Minutes = "Minuten";

        // A block whose topic Knowledge doesn't name (yet).
        public const string UnnamedTopic = "Thema ohne Namen";

        public const string LoadFailed = "Dein Plan für heute ließ sich gerade nicht laden.";

        // After the plan came late, not after a failure (Connection.Retry).
        public const string Reload = "Erneut laden";

        public const string NoTopics = "Für heute ist noch nichts geplant.";

        public const string NoTopicsHint = "Füge einen Text ein, aus dem du lernen willst. Aus seinen Themen entsteht dein Plan.";

        public const string NoTopicsLink = "Text einfügen";

        public const string QuestionsPending = "Deine Fragen entstehen gerade.";

        public const string QuestionsPendingHint =
            "Das dauert meist ein paar Minuten. Schau danach wieder vorbei, dann steht hier dein Plan.";

        public const string QuestionsPendingLink = "Zu deinen Themen";

        public const string AllMastered = "Für heute steht nichts an.";

        public const string AllMasteredHint = "Deine Themen sitzen gerade. Mit einem neuen Text kommen neue Themen dazu.";

        public const string AllMasteredLink = "Neuen Text einfügen";

        // No blocks, for a reason this client doesn't know yet.
        public const string Nothing = "Für heute ist nichts geplant.";

        public const string NothingLink = "Zu deinen Themen";

        public const string MasteryUnavailable = "Dein Lernstand ist gerade nicht abrufbar, die Reihenfolge ist deshalb vorläufig.";

        // Followed by the names of the topics.
        public const string Shortened = "Diese Woche reicht die Zeit nicht für jedes Thema. Ohne Lernzeit bleiben diesmal:";

        // Followed by the names of the topics.
        public const string NotReady = "Zu diesen Themen entstehen die Fragen noch. Sie kommen in den Plan, sobald die Fragen da sind:";
    }

    // The assistant for a new learning goal. Option texts are keyed by the values in LearningGoalValues.
    public static class Goal
    {
        public const string Heading = "Neues Lernziel";

        // {0} is the step, {1} how many there are.
        public const string StepOf = "Schritt {0} von {1}";

        public const string KindStep = "Art des Lernziels";

        public const string AboutStep = "Worum es geht";

        public const string LevelStep = "Stufe und Sprache";

        public const string SummaryStep = "Überblick";

        public const string KindLabel = "Wie lernst du?";

        public const string KindHint = "Bei einer Prüfung fragt Edvaniq nach ihrer Art und, wenn du ihn kennst, nach dem Termin.";

        public const string ExamPreparation = "Prüfungsvorbereitung";

        public const string FreeLearning = "Freies Lernen";

        public const string NameLabel = "Name";

        public const string NameHint = "Zum Beispiel: Biologie-Klausur oder C# lernen.";

        public const string ExamTypeLabel = "Art der Prüfung";

        public const string ExamDateLabel = "Prüfungstermin";

        public const string ExamDateHint = "Freiwillig, wenn du ihn schon kennst.";

        public const string GoalLabel = "Dein Ziel";

        public const string GoalHint = "Freiwillig, zum Beispiel: mindestens eine 2.";

        public const string LevelLabel = "Stufe";

        public const string LevelHint = "Danach richten sich Themen und Fragen.";

        public const string OutputLanguageLabel = "Sprache der Themen und Fragen";

        public const string NoAnswer = "Keine Angabe";

        public const string Next = "Weiter";

        public const string Back = "Zurück";

        public const string Save = "Speichern";

        public const string Saving = "Wird gespeichert …";

        public const string Saved = "Dein Lernziel ist gespeichert.";

        public const string SavedHint = "Füge jetzt einen Text ein, aus dem du lernen willst.";

        public const string SavedLink = "Text einfügen";

        // Only goals after the first: plan, sessions and texts stay with the first one for now.
        public const string NotFirst = "Dein Plan richtet sich vorerst nach deinem ersten Lernziel.";

        public const string SaveFailed = "Dein Lernziel ließ sich gerade nicht speichern.";

        public const string Kept = "Dein Lernziel ist noch nicht gespeichert. Deine Eingaben bleiben hier, bis du dich neu angemeldet hast.";

        public const string DraftBack = "Deine Eingaben von vorhin sind noch da. Prüf sie und speichere das Lernziel.";

        // Same words as Planning's LearningGoalFields, LearningGoalValuesTests checks it.
        public const string KindMissing = "Wähle, ob du auf eine Prüfung lernst oder frei lernst.";

        public const string NameMissing = "Gib dem Lernziel einen Namen.";

        public const string NameTooLong = "Der Name darf höchstens 80 Zeichen lang sein.";

        public const string ExamTypeMissing = "Wähle die Art der Prüfung.";

        public const string GoalTooLong = "Das Ziel darf höchstens 200 Zeichen lang sein.";

        public static readonly IReadOnlyDictionary<string, string> ExamTypes = new Dictionary<string, string>
        {
            ["Ex"] = "Ex (Stegreifaufgabe)",
            ["Schulaufgabe"] = "Schulaufgabe",
            ["Klausur"] = "Klausur",
            ["Ihk"] = "IHK-Prüfung",
            ["Other"] = "Andere Prüfung",
        };

        public static readonly IReadOnlyDictionary<string, string> Levels = new Dictionary<string, string>
        {
            ["LowerSecondary"] = "Unterstufe",
            ["MiddleSecondary"] = "Mittelstufe",
            ["UpperSecondary"] = "Oberstufe",
            ["Apprenticeship"] = "Ausbildung",
            ["University"] = "Studium",
            ["SelfLearner"] = "Selbst lernend",
        };

        public static readonly IReadOnlyDictionary<string, string> OutputLanguages = new Dictionary<string, string>
        {
            ["de"] = "Deutsch",
            ["en"] = "Englisch",
        };
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

        // The button under a view that couldn't load.
        public const string Retry = "Erneut versuchen";
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
