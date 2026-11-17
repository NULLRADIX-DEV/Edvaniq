using Edvaniq.Client.Core;

namespace Edvaniq.Client.UI.Pages;

// A question that comes with what happened to it before: Answered with Correct as it was graded. Fresh ones have neither.
public sealed record RunQuestion(AskedQuestion Asked, bool Answered = false, bool? Correct = null);

// Where a run of questions stands, shared by the block quiz (#357) and the new topic unit (#358). The questions are taken
// once: the card starts afresh whenever it gets another instance, so Current must stay the same object between renders.
public sealed class QuestionRun
{
    private readonly AskedQuestion[] asked;
    private readonly bool[] answeredBefore;

    // How each question went: true right, false wrong or solution shown, null not answered (a type the app can't show).
    private readonly bool?[] outcomes;

    public QuestionRun(IEnumerable<RunQuestion> questions)
    {
        RunQuestion[] all = [.. questions];
        asked = [.. all.Select(question => question.Asked)];
        answeredBefore = [.. all.Select(question => question.Answered)];
        outcomes = [.. all.Select(question => question.Answered ? question.Correct ?? false : (bool?)null)];
        Index = NextOpen(0);
    }

    public int Index { get; private set; }

    public int Count => asked.Length;

    public bool IsDone => Index == Count;

    public AskedQuestion Current => asked[Index];

    // No open question after this one: what was answered before is not asked again.
    public bool IsLast => NextOpen(Index + 1) == Count;

    public QuizTally Tally =>
        new(outcomes.Count(outcome => outcome == true), outcomes.Count(outcome => outcome is not null), outcomes.Length);

    public void Grade(bool correct) => outcomes[Index] = correct;

    public void MoveOn() => Index = NextOpen(Index + 1);

    // The first question from here not answered yet, or the end.
    private int NextOpen(int from)
    {
        var next = from;
        while (next < asked.Length && answeredBefore[next])
        {
            next++;
        }

        return next;
    }
}