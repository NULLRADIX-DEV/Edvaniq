using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// #403: where a topic stood at the start and at the end of a session, the same Assess as /mastery would have given then.
public sealed class MasteryChangeTests
{
    private const string Alice = "alice";

    private static readonly Guid Topic = Guid.NewGuid();
    private static readonly DateTime From = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddMinutes(20);

    [Fact]
    public void Before_IsTheStandingAtTheStart_AfterTheOneAtTheEnd()
    {
        var earlier = Record(From.AddDays(-3), correct: false);
        var session = new[] { Record(From.AddMinutes(5), correct: true), Record(From.AddMinutes(9), correct: true) };
        List<LearningRecord> records = [earlier, .. session];

        var before = MasteryChange.At(records, null, From);
        var after = MasteryChange.At(records, null, To);

        Assert.Equal(TopicStates.Assess([earlier], null, From), before);
        Assert.Equal(TopicStates.Assess(records, null, To), after);
        Assert.True(after!.Mastery > before!.Mastery);
    }

    // Answers given later and the time since change nothing: the same session shows the same change.
    [Fact]
    public void LaterAnswers_AndTime_DontMoveIt()
    {
        List<LearningRecord> records = [Record(From.AddMinutes(5), correct: true)];
        var after = MasteryChange.At(records, null, To);

        records.Add(Record(To.AddHours(2), correct: false));

        Assert.Equal(after, MasteryChange.At(records, null, To));
    }

    // A learn block of a new topic: nothing before, the introduction at the end counts the check questions of its
    // session. The quiz answers of that session have no session and come before the end, so they don't count yet:
    // today's behaviour, #1040 changes it.
    [Fact]
    public void Introduction_CountsAtTheEndOnly()
    {
        var unit = Guid.NewGuid();
        var check = new[] { Record(From.AddMinutes(4), correct: true, session: unit), Record(From.AddMinutes(5), correct: true, session: unit) };
        var quiz = Record(From.AddMinutes(12), correct: false);
        var introduction = TopicIntroduction.Create(Alice, Topic, unit, To);
        List<LearningRecord> records = [.. check, quiz];

        var before = MasteryChange.At(records, introduction, From);
        var after = MasteryChange.At(records, introduction, To);

        Assert.Equal(new TopicStanding(50, TopicState.New), before);
        Assert.Equal(TopicStates.Assess(check, introduction, To), after);
        Assert.NotEqual(TopicState.New, after!.State);
    }

    [Fact]
    public void Carry_BeforeTheStart_CountsAsUsual()
    {
        var carry = MasteryCarry.Restore(Alice, Topic, new MasteryFold(70, From.AddMonths(-13), true, null), Guid.NewGuid(), Mastery.RuleVersion);
        List<LearningRecord> records = [Record(From.AddMinutes(5), correct: true)];

        Assert.Equal(TopicStates.Assess([], null, From, carry: carry), MasteryChange.At(records, null, From, carry));
        Assert.Equal(TopicStates.Assess(records, null, To, carry: carry), MasteryChange.At(records, null, To, carry));
    }

    // The records up to the carry's last one are gone: a moment before it can't be told any more.
    [Fact]
    public void Carry_AfterTheMoment_GivesNull()
    {
        var carry = MasteryCarry.Restore(Alice, Topic, new MasteryFold(70, From.AddMinutes(1), true, null), Guid.NewGuid(), Mastery.RuleVersion);

        Assert.Null(MasteryChange.At([], null, From, carry));
        Assert.NotNull(MasteryChange.At([], null, To, carry));
    }

    private static LearningRecord Record(DateTime answeredAt, bool correct, Guid? session = null) =>
        LearningRecord.Create(Guid.NewGuid(), Alice, Topic, Guid.NewGuid(), session, correct, 2, 3000, null, Mode.Practice, answeredAt);
}