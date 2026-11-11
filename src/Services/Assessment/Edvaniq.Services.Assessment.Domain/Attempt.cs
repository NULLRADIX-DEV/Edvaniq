using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Assessment.Domain;

// A question put to the user and, once given, their answer. Its id is the answerId of the learning record, so however
// often the same answer comes in, it's stored and reported once. Grading is binary: single choice and true/false are
// right with the right option, multiple choice only with exactly the right set, and showing the solution is wrong.
public sealed class Attempt : IOwnedByUser
{
    private Attempt()
    {
    }

    public Guid Id { get; private set; }

    public string OwnerId { get; private set; } = "";

    public Guid QuestionId { get; private set; }

    // Knowledge's topic the question was asked for, only a reference.
    public Guid TopicId { get; private set; }

    // UTC.
    public DateTime AskedAt { get; private set; }

    public bool Answered { get; private set; }

    // The chosen option of a single choice or true/false question.
    public int? Option { get; private set; }

    // The chosen options of a multiple choice question, sorted.
    public IReadOnlyList<int>? Options { get; private set; }

    // The user asked for the solution instead of answering.
    public bool Revealed { get; private set; }

    public bool? Correct { get; private set; }

    public int? ResponseTimeMs { get; private set; }

    // UTC, set by the server.
    public DateTime? AnsweredAt { get; private set; }

    public static Attempt Ask(Question question, Guid topicId, DateTime askedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = question.OwnerId,
            QuestionId = question.Id,
            TopicId = topicId,
            AskedAt = Stored(askedAt),
        };

    public static bool IsOption(Question question, int option) => option >= 0 && option < question.Options.Count;

    // Multiple choice takes one or more options, the others exactly one. The same option twice counts once.
    public static bool IsChoice(Question question, IReadOnlyCollection<int> chosen) =>
        chosen.All(option => IsOption(question, option))
        && chosen.Distinct().Count() is var count
        && (question.Type == QuestionType.MultipleChoice ? count > 0 : count == 1);

    // The client measures the time, but it can't be longer than the server saw pass since the question went out.
    public static int ResponseTime(int measuredMs, DateTime askedAt, DateTime answeredAt)
    {
        var elapsed = Math.Clamp((answeredAt - askedAt).TotalMilliseconds, 0, int.MaxValue);
        return (int)Math.Min(Math.Max(measuredMs, 0), elapsed);
    }

    public void Answer(Question question, int option, int measuredMs, DateTime answeredAt) =>
        Answer(question, [option], measuredMs, answeredAt);

    // Call it only once, with a choice that passed IsChoice. The order of the options doesn't matter.
    public void Answer(Question question, IReadOnlyCollection<int> chosen, int measuredMs, DateTime answeredAt)
    {
        CheckOpen(question);
        if (!IsChoice(question, chosen))
        {
            throw new ArgumentException("Not a choice the question that was asked allows.", nameof(chosen));
        }

        var options = chosen.Distinct().Order().ToList();
        if (question.Type == QuestionType.MultipleChoice)
        {
            Options = options;
        }
        else
        {
            Option = options[0];
        }

        Close(options.SequenceEqual(question.RightOptions), measuredMs, answeredAt);
    }

    // Showing the solution answers the attempt as wrong, once, like any answer.
    public void Reveal(Question question, int measuredMs, DateTime answeredAt)
    {
        CheckOpen(question);
        Revealed = true;
        Close(false, measuredMs, answeredAt);
    }

    private void CheckOpen(Question question)
    {
        if (Answered)
        {
            throw new InvalidOperationException("The attempt is answered already, its answer stays.");
        }

        if (question.Id != QuestionId)
        {
            throw new ArgumentException("Not the question that was asked.", nameof(question));
        }
    }

    private void Close(bool correct, int measuredMs, DateTime answeredAt)
    {
        Answered = true;
        Correct = correct;
        AnsweredAt = Stored(answeredAt);
        ResponseTimeMs = ResponseTime(measuredMs, AskedAt, AnsweredAt.Value);
    }

    // UTC to the microsecond, as the column keeps it. The first answer then reads the same as a stored one.
    private static DateTime Stored(DateTime value) => new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);
}