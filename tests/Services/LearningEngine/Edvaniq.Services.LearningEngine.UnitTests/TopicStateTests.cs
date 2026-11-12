using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// Thresholds 50 and 80 in whole percent, weakness also at 2 wrong of the last 3 answers (only once there are 3).
public sealed class TopicStateTests
{
    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    // A37: four introduced topics without wrong answers lately.
    [Fact]
    public void IntroducedTopics_At49_50_79_80_AreWeakness_Building_Building_Secure()
    {
        var right = Answers(true, true, true);

        Assert.Equal(TopicState.Weakness, TopicStates.Of(true, 49, right));
        Assert.Equal(TopicState.Building, TopicStates.Of(true, 50, right));
        Assert.Equal(TopicState.Building, TopicStates.Of(true, 79, right));
        Assert.Equal(TopicState.Secure, TopicStates.Of(true, 80, right));
    }

    [Fact]
    public void Extremes_AreWeakness_AndSecure()
    {
        Assert.Equal(TopicState.Weakness, TopicStates.Of(true, 0, []));
        Assert.Equal(TopicState.Secure, TopicStates.Of(true, 100, []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(100)]
    public void NotIntroduced_IsNew_WhateverTheMasteryAndAnswers(int mastery)
    {
        Assert.Equal(TopicState.New, TopicStates.Of(false, mastery, []));
        Assert.Equal(TopicState.New, TopicStates.Of(false, mastery, Answers(false, false, false)));
    }

    [Fact]
    public void OneWrongOfTheLastThree_KeepsTheMasteryState()
    {
        Assert.Equal(TopicState.Building, TopicStates.Of(true, 60, Answers(true, false, true)));
        Assert.Equal(TopicState.Secure, TopicStates.Of(true, 90, Answers(false, true, true)));
    }

    [Fact]
    public void TwoWrongOfTheLastThree_IsWeakness_EvenWhenTheMasteryIsHigh()
    {
        Assert.Equal(TopicState.Weakness, TopicStates.Of(true, 60, Answers(true, false, false)));
        Assert.Equal(TopicState.Weakness, TopicStates.Of(true, 90, Answers(false, true, false)));
        Assert.Equal(TopicState.Weakness, TopicStates.Of(true, 90, Answers(false, false, false)));
    }

    // Fewer than 3 answers are too few for an error rate, the mastery alone decides.
    [Fact]
    public void TwoAnswersBothWrong_AreNoErrorRateYet()
    {
        Assert.False(TopicStates.HasHighRecentErrorRate(Answers(false, false)));
        Assert.Equal(TopicState.Building, TopicStates.Of(true, 60, Answers(false, false)));
    }

    [Fact]
    public void OnlyTheLastThreeAnswersCount()
    {
        Assert.False(TopicStates.HasHighRecentErrorRate(Answers(false, false, false, true, true, true)));
        Assert.True(TopicStates.HasHighRecentErrorRate(Answers(true, true, true, false, true, false)));
    }

    // The input order doesn't matter, "last" goes by AnsweredAt like the mastery.
    [Fact]
    public void LastAnswers_GoByTime_NotByListOrder()
    {
        var answers = Answers(false, false, true, true, true);

        Assert.False(TopicStates.HasHighRecentErrorRate(answers.AsEnumerable().Reverse()));
    }

    // The transition until #396: without an introduction the first learning record introduces the topic.
    [Fact]
    public void WithoutIntroduction_IsNew_UntilTheFirstRecordIntroducesIt()
    {
        Assert.Equal(TopicState.New, State([], Monday));
        Assert.Equal(TopicState.Building, State(Answers(true), Monday));
        Assert.Equal(TopicState.Weakness, State(Answers(false), Monday));
    }

    // From the records the state follows the shown mastery: 3 right medium answers give 78.9 (79), a fourth 84.2.
    [Fact]
    public void FromRecords_TheStateFollowsTheShownMastery()
    {
        var three = Answers(true, true, true);
        var four = Answers(true, true, true, true);

        Assert.Equal(79, Mastery.Percent(Mastery.Of(three, Monday)));
        Assert.Equal(TopicState.Building, State(three, Monday));
        Assert.Equal(84, Mastery.Percent(Mastery.Of(four, Monday)));
        Assert.Equal(TopicState.Secure, State(four, Monday));
    }

    // A39: lately only wrong, but the mastery is still above 50.
    [Fact]
    public void FromRecords_WrongAnswersLately_MakeAWeakness_AboveFiftyPercent()
    {
        var records = Enumerable.Repeat(true, 8).Select(_ => (Correct: true, Difficulty: 3))
            .Append((Correct: false, Difficulty: 1))
            .Append((Correct: false, Difficulty: 1))
            .Select((answer, minute) => Answer(answer.Correct, Monday.AddMinutes(minute), answer.Difficulty))
            .ToList();
        var now = Monday.AddMinutes(records.Count);

        Assert.True(Mastery.Percent(Mastery.Of(records, now)) > 50);
        Assert.Equal(TopicState.Weakness, State(records, now));
    }

    // AK1: every mastery and every short history gives exactly one state, and New only when not introduced.
    [Fact]
    public void EveryInput_GivesExactlyOneDefinedState()
    {
        for (var length = 0; length <= 4; length++)
        {
            for (var pattern = 0; pattern < 1 << length; pattern++)
            {
                var history = Answers([.. Enumerable.Range(0, length).Select(bit => (pattern & 1 << bit) != 0)]);
                for (var mastery = 0; mastery <= 100; mastery++)
                {
                    foreach (var introduced in new[] { false, true })
                    {
                        var state = TopicStates.Of(introduced, mastery, history);

                        Assert.True(Enum.IsDefined(state));
                        Assert.Equal(!introduced, state == TopicState.New);
                    }
                }
            }
        }
    }

    private static TopicState State(List<LearningRecord> records, DateTime now) =>
        TopicStates.Assess(records, null, now).State;

    private static List<LearningRecord> Answers(params bool[] correct) =>
        [.. correct.Select((right, minute) => Answer(right, Monday.AddMinutes(minute)))];

    private static LearningRecord Answer(bool correct, DateTime answeredAt, int difficulty = 2) =>
        LearningRecord.Create(
            Guid.NewGuid(), "alice", Guid.Empty, Guid.NewGuid(), null, correct, difficulty, 4200, null, Mode.Practice, answeredAt);
}