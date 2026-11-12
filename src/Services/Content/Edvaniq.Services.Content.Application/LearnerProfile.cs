namespace Edvaniq.Services.Content.Application;

// Who the prompts write for: level and output language of the learning goal a text was pasted into. Planning's values
// arrive as plain names (rule 6), anything unknown or missing gives the default, German at middle secondary level.
public sealed record LearnerProfile(LearnerLevel Level, string Language)
{
    public const string German = "de";

    public const string English = "en";

    public static readonly LearnerProfile Default = new(LearnerLevel.MiddleSecondary, German);

    public static readonly IReadOnlyList<string> Languages = [German, English];

    // Every profile a prompt can be built for, in a fixed order, so the prompt tests cover each variant.
    public static IEnumerable<LearnerProfile> All =>
        Enum.GetValues<LearnerLevel>().SelectMany(level => Languages.Select(language => new LearnerProfile(level, language)));

    public static LearnerProfile Of(string? level, string? language) =>
        new(
            level is not null && Enum.GetNames<LearnerLevel>().Contains(level) ? Enum.Parse<LearnerLevel>(level) : Default.Level,
            language is not null && Languages.Contains(language) ? language : Default.Language);

    // The language as the German prompt names it.
    internal string LanguageName => Language == English ? "Englisch" : "Deutsch";

    // How the names of the topics fit the level. Only the wording, how the material splits stays the material's.
    internal string TopicNameInstruction => Level switch
    {
        LearnerLevel.LowerSecondary =>
            "Die Lernenden sind in der Unterstufe (etwa Klasse 5 bis 7). Wähle für die Namen einfache, kurze Wörter, die sie verstehen.",
        LearnerLevel.MiddleSecondary =>
            "Die Lernenden sind in der Mittelstufe (etwa Klasse 8 bis 10). Wähle für die Namen klare Wörter und die Grundbegriffe des Fachs.",
        LearnerLevel.UpperSecondary =>
            "Die Lernenden sind in der Oberstufe. Verwende für die Namen die Fachsprache.",
        LearnerLevel.Apprenticeship =>
            "Die Lernenden sind in der Berufsausbildung. Verwende für die Namen die Fachsprache des Berufs.",
        LearnerLevel.University =>
            "Die Lernenden studieren. Verwende für die Namen die wissenschaftliche Fachsprache.",
        LearnerLevel.SelfLearner =>
            "Die Lernenden lernen selbstständig, ohne Schule oder Ausbildung. Wähle für die Namen verständliche Wörter, ohne Vorwissen vorauszusetzen.",
        _ => throw new ArgumentOutOfRangeException(nameof(Level), Level, null),
    };

    // How the questions fit the level. Plain about what to ask, never a judgement of the learner.
    internal string LevelInstruction => Level switch
    {
        LearnerLevel.LowerSecondary =>
            "Die Lernenden sind in der Unterstufe (etwa Klasse 5 bis 7). Formuliere einfach und in kurzen Sätzen, erkläre jeden Fachbegriff in der Erklärung und frage vor allem nach Wissen und einfachem Verständnis.",
        LearnerLevel.MiddleSecondary =>
            "Die Lernenden sind in der Mittelstufe (etwa Klasse 8 bis 10). Formuliere klar, setze die Grundbegriffe des Fachs voraus und frage nach Wissen, Verständnis und einfacher Anwendung.",
        LearnerLevel.UpperSecondary =>
            "Die Lernenden sind in der Oberstufe. Verwende die Fachsprache und frage auch nach Zusammenhängen, Begründungen und der Anwendung auf neue Fälle.",
        LearnerLevel.Apprenticeship =>
            "Die Lernenden sind in der Berufsausbildung. Formuliere praxisnah, verwende die Fachsprache des Berufs und frage, wo das Material es hergibt, nach der Anwendung in typischen Situationen der Arbeit.",
        LearnerLevel.University =>
            "Die Lernenden studieren. Verwende die wissenschaftliche Fachsprache und frage auch nach Zusammenhängen, Abgrenzungen und der Einordnung von Aussagen.",
        LearnerLevel.SelfLearner =>
            "Die Lernenden lernen selbstständig, ohne Schule oder Ausbildung. Formuliere verständlich, ohne Vorwissen vorauszusetzen, erkläre Fachbegriffe kurz und frage nach Wissen und Verständnis.",
        _ => throw new ArgumentOutOfRangeException(nameof(Level), Level, null),
    };
}

// The levels of a learning goal, by the names Planning gives them.
public enum LearnerLevel
{
    LowerSecondary,
    MiddleSecondary,
    UpperSecondary,
    Apprenticeship,
    University,
    SelfLearner,
}