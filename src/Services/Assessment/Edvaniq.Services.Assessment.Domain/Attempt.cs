using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Assessment.Domain;

// A question put to the user and, once given, their answer. Its id is the answerId of the learning record, so however
// often the same answer comes in, it's stored and reported once. The question is single choice: one option is right.
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

    public int? Option { get; private set; }

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

    // The client measures the time, but it can't be longer than the server saw pass since the question went out.
    public static int ResponseTime(int measuredMs, DateTime askedAt, DateTime answeredAt)
    {
        var elapsed = Math.Clamp((answeredAt - askedAt).TotalMilliseconds, 0, int.MaxValue);
        return (int)Math.Min(Math.Max(measuredMs, 0), elapsed);
    }

    // Call it only once, with an option that passed IsOption.
    public void Answer(Question question, int option, int measuredMs, DateTime answeredAt)
    {
        if (Answered)
        {
            throw new InvalidOperationException("The attempt is answered already, its answer stays.");
        }

        if (question.Id != QuestionId || !IsOption(question, option))
        {
            throw new ArgumentException("Not an option of the question that was asked.", nameof(option));
        }

        Answered = true;
        Option = option;
        Correct = option == question.Answer;
        AnsweredAt = Stored(answeredAt);
        ResponseTimeMs = ResponseTime(measuredMs, AskedAt, AnsweredAt.Value);
    }

    // UTC to the microsecond, as the column keeps it. The first answer then reads the same as a stored one.
    private static DateTime Stored(DateTime value) => new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);
}