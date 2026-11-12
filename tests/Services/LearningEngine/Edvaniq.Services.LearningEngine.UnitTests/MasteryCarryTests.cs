using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// #365 AC3: expired records go into the carry of their topic, and mastery, state, decay and last answer come out exactly
// as if the records were still there.
public sealed class MasteryCarryTests
{
    private const string Alice = "alice";

    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateTime Start = new(2025, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Session = Guid.NewGuid();

    public static TheoryData<int, int> Splits()
    {
        var data = new TheoryData<int, int>();
        for (var split = 0; split <= Answers().Count; split++)
        {
            data.Add(split, 1);
            data.Add(split, 3);
        }

        return data;
    }

    // At every point the records may be split and in portions of any size: the same standing at several times later.
    [Theory]
    [MemberData(nameof(Splits))]
    public void FoldedRecords_GiveTheSameStanding_AsAllRecords(int split, int portion)
    {
        var records = Answers();
        var (carry, rest) = FoldFirst(records, split, portion, introduction: null);

        foreach (var now in Times(records))
        {
            Assert.Equal(TopicStates.Assess(records, null, now), TopicStates.Assess(rest, null, now, carry: carry));
            Assert.Equal(TopicStates.Decayed(records, null, now), TopicStates.Decayed(rest, null, now, carry: carry));
            Assert.Equal(Mastery.Of(records, now), Mastery.Of(rest, now, from: carry?.FoldFor(null)));
        }

        Assert.Equal(LearningRecord.Latest(records), LearningRecord.Latest(rest, carry));
    }

    // The same with an introduction: answers before it don't count, the check questions of its session do.
    [Theory]
    [MemberData(nameof(Splits))]
    public void FoldedRecords_WithAnIntroduction_GiveTheSameStanding(int split, int portion)
    {
        var records = Answers();
        var introduction = TopicIntroduction.Create(Alice, Topic, Session, Start.AddDays(20));
        var (carry, rest) = FoldFirst(records, split, portion, introduction);

        foreach (var now in Times(records))
        {
            Assert.Equal(TopicStates.Assess(records, introduction, now), TopicStates.Assess(rest, introduction, now, carry: carry));
            Assert.Equal(TopicStates.Decayed(records, introduction, now), TopicStates.Decayed(rest, introduction, now, carry: carry));
        }

        Assert.Equal(LearningRecord.Latest(records), LearningRecord.Latest(rest, carry));
    }

    // Everything expired: the topic stays a weakness at its low value instead of starting over as new at 50.
    [Fact]
    public void AllRecordsGone_TheTopicDoesNotStartOver()
    {
        var records = Answers();
        var now = records.Max(record => record.AnsweredAt).AddMonths(13);
        var carry = MasteryCarry.Fold(null, Alice, Topic, records, null);

        var standing = TopicStates.Assess([], null, now, carry: carry);

        Assert.Equal(TopicStates.Assess(records, null, now), standing);
        Assert.Equal(TopicState.Weakness, standing.State);
        Assert.True(standing.Mastery < 10);
        Assert.Equal(new TopicStanding(50, TopicState.New), TopicStates.Assess([], null, now));
    }

    // The window of the last three answers reaches over the records that went: two wrong before, one right after.
    [Fact]
    public void RecentErrorRate_ReachesIntoTheCarry()
    {
        var at = Start.AddDays(100);
        LearningRecord[] records =
        [
            Record(true, at, 3), Record(true, at.AddMinutes(1), 3), Record(true, at.AddMinutes(2), 3),
            Record(false, at.AddMinutes(3), 1), Record(false, at.AddMinutes(4), 1), Record(true, at.AddMinutes(5), 3),
        ];
        var carry = MasteryCarry.Fold(null, Alice, Topic, records[..5], null);
        var now = at.AddMinutes(6);

        Assert.True(TopicStates.HasHighRecentErrorRate(records));
        Assert.Equal(TopicState.Weakness, TopicStates.Assess(records, null, now).State);
        Assert.Equal(TopicStates.Assess(records, null, now), TopicStates.Assess(records[5..], null, now, carry: carry));
    }

    // An introduction that comes after the carry means none of its records count, as with the records themselves.
    [Fact]
    public void IntroductionAfterTheCarry_LeavesItsRecordsUncounted()
    {
        var records = Answers();
        var carry = MasteryCarry.Fold(null, Alice, Topic, records[..4], null);
        var introduction = TopicIntroduction.Create(Alice, Topic, Guid.NewGuid(), records[3].AnsweredAt.AddDays(1));
        var now = records[^1].AnsweredAt.AddDays(2);

        Assert.Null(carry.FoldFor(introduction));
        Assert.Equal(TopicStates.Assess(records, introduction, now), TopicStates.Assess(records[4..], introduction, now, carry: carry));
    }

    // Only records before the introduction went: nothing counted, the carry keeps only the last answer.
    [Fact]
    public void OnlyUncountedRecordsGone_TheCarryHasNoFold()
    {
        var records = Answers();
        var introduction = TopicIntroduction.Create(Alice, Topic, Session, Start.AddDays(20));
        var before = records.Where(record => !introduction.Counts(record)).ToList();

        var carry = MasteryCarry.Fold(null, Alice, Topic, before, introduction);

        Assert.Null(carry.FoldFor(introduction));
        Assert.Equal(before[^1].AnswerId, carry.LastAnswerId);
    }

    // A carry folded under an older rule is read as it is, the current rule goes on from it.
    [Fact]
    public void CarryOfAnOlderRule_IsStillRead()
    {
        var fold = new MasteryFold(90, Start, LastCorrect: true, PreviousCorrect: true);
        var carry = MasteryCarry.Restore(Alice, Topic, fold, Guid.NewGuid(), ruleVersion: Mastery.RuleVersion - 1);
        var answer = Record(true, Start.AddDays(1), 2);

        var standing = TopicStates.Assess([answer], null, answer.AnsweredAt, carry: carry);

        Assert.Equal(Mastery.RuleVersion - 1, carry.RuleVersion);
        Assert.Equal(Mastery.Percent(Mastery.Of([answer], answer.AnsweredAt, from: fold)), standing.Mastery);
        Assert.Equal(TopicState.Secure, standing.State);
        Assert.Equal(answer.AnswerId, LearningRecord.Latest([answer], carry));
        Assert.Equal(carry.LastAnswerId, LearningRecord.Latest([], carry));
    }

    [Fact]
    public void Fold_StampsTheCurrentRule_AndCountsUpItsVersion()
    {
        var records = Answers();
        var first = MasteryCarry.Fold(null, Alice, Topic, records[..2], null);
        var version = first.Version;
        var second = MasteryCarry.Fold(first, Alice, Topic, records[2..4], null);

        Assert.Same(first, second);
        Assert.Equal(version + 1, second.Version);
        Assert.Equal(Mastery.RuleVersion, second.RuleVersion);
        Assert.Throws<ArgumentException>(() => MasteryCarry.Fold(null, Alice, Topic, [], null));
    }

    // Folds the first split records in portions, the way the retention run does, and returns the rest.
    private static (MasteryCarry? Carry, List<LearningRecord> Remaining) FoldFirst(
        List<LearningRecord> records, int split, int portion, TopicIntroduction? introduction)
    {
        var ordered = records.OrderBy(record => record.AnsweredAt).ThenBy(record => record.AnswerId).ToList();
        MasteryCarry? carry = null;
        foreach (var chunk in ordered.Take(split).Chunk(portion))
        {
            carry = MasteryCarry.Fold(carry, Alice, Topic, chunk, introduction);
        }

        return (carry, ordered.Skip(split).ToList());
    }

    private static IEnumerable<DateTime> Times(List<LearningRecord> records)
    {
        var last = records.Max(record => record.AnsweredAt);
        return [last, last.AddDays(3), last.AddDays(40), last.AddMonths(13)];
    }

    // A year and a half of practice: some wrong, all difficulties, two at the same time, the introduction's session.
    private static List<LearningRecord> Answers() =>
    [
        Record(true, Start, 2),
        Record(false, Start.AddDays(2), 1),
        Record(true, Start.AddDays(9), 3),
        Record(false, Start.AddDays(19), 2, Session),
        Record(true, Start.AddDays(20), 2, Session),
        Record(true, Start.AddDays(20), 3),
        Record(false, Start.AddDays(45), 1),
        Record(false, Start.AddDays(46), 2),
        Record(true, Start.AddDays(120), null),
        Record(true, Start.AddDays(200), 3),
        Record(true, Start.AddDays(201), 3),
        Record(false, Start.AddDays(400), 2),
        Record(true, Start.AddDays(530), 2),
    ];

    private static LearningRecord Record(bool correct, DateTime answeredAt, int? difficulty, Guid? session = null) =>
        LearningRecord.Create(Guid.NewGuid(), Alice, Topic, Guid.NewGuid(), session, correct, difficulty, 3000, null, Mode.Practice, answeredAt);
}