using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// A41: mastery is never stored, every answer of LearningEngine works it out again from the learning records. So the
// same records at the same time have to give the same value, whatever order they come in and however often (#362).
public sealed class RecomputeTests
{
    private static readonly DateTime Introduced = new(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Session = Guid.NewGuid();

    [Fact]
    public void WeeksWithoutReview_LowerIt_AndRecomputingGivesTheSameValue()
    {
        var records = AnswersOverWeeks();
        var introduction = TopicIntroduction.Create("alice", Guid.Empty, Session, Introduced);
        var lastAnswer = records.Max(record => record.AnsweredAt);
        var weeksLater = lastAnswer.AddDays(28);

        var rightAfter = TopicStates.Assess(records, introduction, lastAnswer);
        var first = TopicStates.Assess(records, introduction, weeksLater);
        var recomputed = TopicStates.Assess(Copies(records), Copy(introduction), weeksLater);

        Assert.True(first.Mastery < rightAfter.Mastery, $"{first.Mastery} should be below {rightAfter.Mastery}.");
        Assert.Equal(first, recomputed);
        Assert.Equal(Mastery.Of(records, weeksLater), Mastery.Of(Copies(records), weeksLater));
    }

    // Records come back from the database in any order, two answers can share a time: AnswerId decides then.
    [Fact]
    public void ShuffledRecords_AndAnswersAtTheSameTime_GiveTheSameStanding()
    {
        var records = AnswersOverWeeks();
        var sameTime = records[^1].AnsweredAt;
        records.Add(Record(false, sameTime));
        records.Add(Record(true, sameTime));
        var introduction = TopicIntroduction.Create("alice", Guid.Empty, Session, Introduced);
        var now = sameTime.AddDays(5);
        var expected = TopicStates.Assess(records, introduction, now);

        for (var seed = 0; seed < 20; seed++)
        {
            var shuffled = Copies(records).ToArray();
            new Random(seed).Shuffle(shuffled);

            Assert.Equal(expected, TopicStates.Assess(shuffled, introduction, now));
        }
    }

    // Four weeks of practice after the introduction, one answer every few days, some wrong, all difficulties.
    private static List<LearningRecord> AnswersOverWeeks() =>
    [
        Record(true, Introduced, difficulty: 1, session: Session),
        Record(false, Introduced.AddMinutes(2), difficulty: 2, session: Session),
        Record(true, Introduced.AddDays(3)),
        Record(true, Introduced.AddDays(6), difficulty: 3),
        Record(false, Introduced.AddDays(10), difficulty: 1),
        Record(true, Introduced.AddDays(13)),
        Record(true, Introduced.AddDays(17), difficulty: 3),
        Record(true, Introduced.AddDays(20)),
        Record(false, Introduced.AddDays(24), difficulty: 3),
        Record(true, Introduced.AddDays(27)),
    ];

    // New objects with the same values, like a second read from the database.
    private static List<LearningRecord> Copies(IEnumerable<LearningRecord> records) =>
    [
        .. records.Select(record => LearningRecord.Create(
            record.AnswerId, record.OwnerId, record.TopicId, record.ItemId, record.SessionId, record.Correct,
            record.Difficulty, record.ResponseTimeMs, record.Confidence, record.Source, record.AnsweredAt)),
    ];

    private static TopicIntroduction Copy(TopicIntroduction introduction) =>
        TopicIntroduction.Create(introduction.OwnerId, introduction.TopicId, introduction.SessionId, introduction.IntroducedAt);

    private static LearningRecord Record(bool correct, DateTime answeredAt, int difficulty = 2, Guid? session = null) =>
        LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), session ?? Guid.NewGuid(), correct, difficulty, 4200, null,
            Mode.Practice, answeredAt);
}