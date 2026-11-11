using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// A topic is decayed when it was secure at its last answer and isn't now: only the decay since then moved it.
public sealed class DecayedTopicTests
{
    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    // Four right medium answers give 84 (TopicStateTests), secure.
    private static readonly List<LearningRecord> Secure = Answers(true, true, true, true);

    private static readonly DateTime LastAnswer = Monday.AddMinutes(3);

    [Fact]
    public void SecureTopic_LeftAloneForAMonth_IsDecayed()
    {
        var now = LastAnswer.AddDays(30);

        var decayed = TopicStates.Decayed(Secure, null, now);

        Assert.NotNull(decayed);
        Assert.Equal(TopicStates.Assess(Secure, null, now).Mastery, decayed.Mastery);
        Assert.True(decayed.Mastery < TopicStates.SecureFrom);
        Assert.Equal(84, decayed.MasteryAtLastAnswer);
        Assert.Equal(LastAnswer, decayed.LastAnsweredAt);
    }

    [Fact]
    public void SecureTopic_RightAfterPractice_IsNotDecayed() =>
        Assert.Null(TopicStates.Decayed(Secure, null, LastAnswer.AddHours(1)));

    // AK3: the threshold is TopicStates.SecureFrom. The last hour at 80 isn't decayed, the first one at 79 is.
    [Fact]
    public void Boundary_80IsNotDecayed_79Is()
    {
        var hours = 0;
        while (TopicStates.Assess(Secure, null, LastAnswer.AddHours(hours + 1)).Mastery >= TopicStates.SecureFrom)
        {
            hours++;
        }

        var lastAt80 = LastAnswer.AddHours(hours);
        var firstAt79 = LastAnswer.AddHours(hours + 1);
        Assert.Equal(TopicStates.SecureFrom, TopicStates.Assess(Secure, null, lastAt80).Mastery);
        Assert.Equal(TopicStates.SecureFrom - 1, TopicStates.Assess(Secure, null, firstAt79).Mastery);
        Assert.Null(TopicStates.Decayed(Secure, null, lastAt80));
        Assert.Equal(TopicStates.SecureFrom - 1, TopicStates.Decayed(Secure, null, firstAt79)?.Mastery);
    }

    // Decayed all the way to a weakness still counts, it was secure without the decay.
    [Fact]
    public void SecureTopic_DecayedBelowFifty_IsStillDecayed()
    {
        var now = LastAnswer.AddDays(120);

        Assert.Equal(TopicState.Weakness, TopicStates.Assess(Secure, null, now).State);
        Assert.NotNull(TopicStates.Decayed(Secure, null, now));
    }

    // AK1: only topics that would be above the threshold without the decay. Three right answers give 79.
    [Fact]
    public void TopicBuildingAtItsLastAnswer_IsNotDecayed()
    {
        var building = Answers(true, true, true);

        Assert.Equal(TopicState.Building, TopicStates.Assess(building, null, Monday.AddMinutes(2)).State);
        Assert.Null(TopicStates.Decayed(building, null, Monday.AddDays(30)));
    }

    // A weakness by its recent wrong answers stays one, whatever the mastery was. The plan has it already.
    [Fact]
    public void WeaknessByErrorRateAtItsLastAnswer_IsNotDecayed()
    {
        var records = Enumerable.Range(0, 8).Select(minute => Answer(true, Monday.AddMinutes(minute), 3))
            .Append(Answer(false, Monday.AddMinutes(8), 1))
            .Append(Answer(false, Monday.AddMinutes(9), 1))
            .ToList();

        Assert.Equal(TopicState.Weakness, TopicStates.Assess(records, null, Monday.AddMinutes(9)).State);
        Assert.Null(TopicStates.Decayed(records, null, Monday.AddDays(30)));
    }

    [Fact]
    public void TopicNeverPractised_IsNotDecayed() =>
        Assert.Null(TopicStates.Decayed([], null, Monday.AddDays(365)));

    // Answers before the introduction don't count, so an introduced topic without counted answers has no last answer.
    [Fact]
    public void IntroducedTopic_WithOnlyAnswersFromBefore_IsNotDecayed()
    {
        var introduction = TopicIntroduction.Create("alice", Guid.Empty, Guid.NewGuid(), Monday.AddDays(1));

        Assert.Null(TopicStates.Decayed(Secure, introduction, Monday.AddDays(60)));
    }

    // With an introduction only the counted answers decide, the last of them is the point without decay.
    [Fact]
    public void IntroducedTopic_UsesItsCountedAnswers()
    {
        var introduction = TopicIntroduction.Create("alice", Guid.Empty, Guid.NewGuid(), Monday.AddDays(-1));
        var now = LastAnswer.AddDays(30);

        Assert.Equal(TopicStates.Decayed(Secure, null, now), TopicStates.Decayed(Secure, introduction, now));
    }

    private static List<LearningRecord> Answers(params bool[] correct) =>
        [.. correct.Select((right, minute) => Answer(right, Monday.AddMinutes(minute)))];

    private static LearningRecord Answer(bool correct, DateTime answeredAt, int difficulty = 2) =>
        LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), null, correct, difficulty, 4200, null, Mode.Practice, answeredAt);
}