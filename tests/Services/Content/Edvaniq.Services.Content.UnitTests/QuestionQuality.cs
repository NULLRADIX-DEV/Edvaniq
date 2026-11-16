using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Content.UnitTests;

// #342: plain numbers on a question pool against its source sections (tests/ReferenceSet/README.md, "Fragenqualität").
// Words count once they are content words of at least four letters, compared by their first six letters, so "Chloroplast"
// finds "Chloroplasten". No AI and the same on every run: a hint where to look, the judgment comes from the sample by hand.
internal static class QuestionQuality
{
    public const double MinSourced = 0.5;

    public const double MaxOptionOverlap = 0.8;

    public const double MinExplained = 0.5;

    public const double LongestFactor = 1.3;

    private const int StemLength = 6;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "aber", "alle", "allem", "allen", "aller", "alles", "also", "auch", "aussage", "beim", "beschreibt", "bezeichnet",
        "dabei", "damit", "dann", "darf", "dass", "dein", "deine", "denn", "diese", "diesem", "diesen", "dieser", "dieses",
        "doch", "durch", "eine", "einem", "einen", "einer", "eines", "etwa", "falsch", "folgende", "folgenden", "folgender",
        "gibt", "gilt", "haben", "hier", "immer", "jede", "jedem", "jeden", "jeder", "jedes", "kann", "keine", "können",
        "mehr", "muss", "nach", "nicht", "noch", "oder", "ohne", "sehr", "sich", "sind", "sowie", "stimmt",
        "treffen", "trifft", "über", "unter", "viel", "viele", "wahr", "warum", "weil", "welche", "welchem", "welchen",
        "welcher", "welches", "wenn", "werden", "wird", "wurde", "wurden", "zwei", "zwischen",
    };

    public static QuestionFlags Of(QuestionDraft question, string source)
    {
        var right = question.Answers.Select(index => question.Options[index]).ToList();
        var choice = question.Type != QuestionType.TrueFalse;
        var asked = choice ? right.Prepend(question.Text) : [question.Text];
        var wrong = question.Options.Where((_, index) => !question.Answers.Contains(index)).ToList();

        return new QuestionFlags(
            Coverage(ContentWords(string.Join(' ', asked)), Stems(source)) >= MinSourced,
            choice ? right.All(answer => wrong.All(other => Overlap(answer, other) < MaxOptionOverlap)) : null,
            choice ? Coverage(ContentWords(string.Join(' ', right)), Stems(question.Explanation)) >= MinExplained : null,
            question.Type == QuestionType.SingleChoice && wrong.Count >= 2
                ? right[0].Trim().Length >= LongestFactor * wrong.Max(other => other.Trim().Length)
                : null,
            question.Text.Trim().Length >= 15 && question.Explanation.Trim().Length >= 20
                && (!choice || question.Text.Contains('?', StringComparison.Ordinal) || question.Text.TrimEnd().EndsWith(':')));
    }

    public static bool Sourced(CardDraft card, string source) =>
        Coverage(ContentWords(card.Front + ' ' + card.Back), Stems(source)) >= MinSourced;

    // The share of the words found in the text. Without any word there is nothing to miss.
    public static double Coverage(IReadOnlyCollection<string> words, IReadOnlySet<string> text) =>
        words.Count == 0 ? 1 : (double)words.Count(text.Contains) / words.Count;

    public static IReadOnlyCollection<string> ContentWords(string text) => [.. Stems(text)];

    public static IReadOnlySet<string> Stems(string text) =>
        QuestionGeneration.SameQuestion(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 4 && !StopWords.Contains(word))
            .Select(word => word.Length > StemLength ? word[..StemLength] : word)
            .ToHashSet(StringComparer.Ordinal);

    // Jaccard over every word of the two options, stop words too: "Licht und Wasser" and "Licht und Wärme" are close.
    private static double Overlap(string one, string other)
    {
        var a = AllWords(one);
        var b = AllWords(other);
        var union = a.Union(b).Count();
        return union == 0 ? 1 : (double)a.Intersect(b).Count() / union;
    }

    private static HashSet<string> AllWords(string text) =>
        QuestionGeneration.SameQuestion(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}

// Null where the check doesn't apply: true/false has no options of its own to tell apart or to explain.
internal sealed record QuestionFlags(bool Sourced, bool? Distinct, bool? Explained, bool? LongestRight, bool WellFormed);

// The numbers of one document over all its pools.
internal sealed class QualityTally
{
    public int Questions { get; private set; }

    public int Cards { get; private set; }

    public int Unusable { get; private set; }

    public Quota Sourced { get; } = new();

    public Quota Distinct { get; } = new();

    public Quota Explained { get; } = new();

    public Quota LongestRight { get; } = new();

    public Quota WellFormed { get; } = new();

    public Quota CardsSourced { get; } = new();

    public Dictionary<QuestionType, int> Types { get; } = Enum.GetValues<QuestionType>().ToDictionary(type => type, _ => 0);

    public int[] Difficulties { get; } = new int[3];

    public void Add(IReadOnlyList<QuestionDraft>? questions, IReadOnlyList<CardDraft> cards, IReadOnlyDictionary<Guid, string> sources)
    {
        if (questions is null)
        {
            Unusable++;
            return;
        }

        foreach (var question in questions)
        {
            var flags = QuestionQuality.Of(question, sources[question.SectionId]);
            Questions++;
            Types[question.Type]++;
            Difficulties[question.Difficulty - 1]++;
            Sourced.Add(flags.Sourced);
            WellFormed.Add(flags.WellFormed);
            Distinct.Add(flags.Distinct);
            Explained.Add(flags.Explained);
            LongestRight.Add(flags.LongestRight);
        }

        foreach (var card in cards)
        {
            Cards++;
            CardsSourced.Add(QuestionQuality.Sourced(card, sources[card.SectionId]));
        }
    }
}

// How many of those the check applies to have the flag.
internal sealed class Quota
{
    public int Yes { get; private set; }

    public int Of { get; private set; }

    public double? Rate => Of == 0 ? null : (double)Yes / Of;

    public void Add(bool? flag)
    {
        if (flag is { } value)
        {
            Of++;
            Yes += value ? 1 : 0;
        }
    }
}