using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #408: the bonus review asks about the weakest topic and is no block of the plan.
public class BonusReviewTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly DateTime Now = new(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc);

    // AC1: of the weaknesses the one with the lowest mastery, even if a topic building up is lower.
    [Fact]
    public void Weakness_WithTheLowestMastery_Wins()
    {
        var standings = Standings((A, 30, TopicState.Weakness), (B, 20, TopicState.Weakness), (C, 10, TopicState.Building));

        Assert.Equal(B, BonusReview.TopicOf([A, B, C], standings, []));
    }

    [Fact]
    public void Tie_GoesByTopicId()
    {
        var standings = Standings((B, 20, TopicState.Weakness), (A, 20, TopicState.Weakness));

        Assert.Equal(A, BonusReview.TopicOf([B, A], standings, []));
    }

    // Without a weakness the introduced topic with the lowest mastery, a new one never: it has no unit behind it.
    [Fact]
    public void WithoutWeakness_TheWeakestIntroducedTopic()
    {
        var standings = Standings((A, 90, TopicState.Secure), (B, 5, TopicState.New), (C, 60, TopicState.Building));

        Assert.Equal(C, BonusReview.TopicOf([A, B, C], standings, []));
    }

    // A state Planning doesn't know is no "new": the topic counts with its mastery.
    [Fact]
    public void UnknownState_CountsAsIntroduced()
    {
        var standings = Standings((A, 70, TopicState.Building), (B, 40, null));

        Assert.Equal(B, BonusReview.TopicOf([A, B], standings, []));
    }

    // Only topics with questions, only topics of the goal, only topics LearningEngine knows.
    [Fact]
    public void TopicsWithoutQuestions_OrOutsideTheGoal_OrUnknown_AreLeftOut()
    {
        var standings = Standings((A, 10, TopicState.Weakness), (B, 20, TopicState.Weakness), (C, 5, TopicState.Weakness));

        Assert.Equal(B, BonusReview.TopicOf([A, B, Guid.NewGuid()], standings, [A]));
    }

    // AC3: nothing fits, no topic.
    [Fact]
    public void NothingIntroducedWithQuestions_IsNoTopic()
    {
        Assert.Null(BonusReview.TopicOf([], Standings(), []));
        Assert.Null(BonusReview.TopicOf([A, B], Standings((A, 0, TopicState.New)), []));
        Assert.Null(BonusReview.TopicOf([A], Standings((A, 40, TopicState.Weakness)), [A]));
    }

    [Fact]
    public void BonusSession_IsAReviewWithoutIntroduction()
    {
        var block = Guid.NewGuid();
        var session = Session.StartBonus(Guid.NewGuid(), "alice", Goal, block, new DateOnly(2026, 11, 16), A, Now);

        Assert.True(session.IsBonus);
        Assert.Equal((block, A, BlockKind.Review, BonusReview.Minutes), (session.BlockId, session.TopicId, session.Kind, session.Minutes));
        Assert.Equal([SessionStepKind.Review, SessionStepKind.Quiz], session.Steps.Select(step => step.Kind));
        Assert.Equal(SessionSteps.QuizQuestions, session.Steps[^1].Questions);
    }

    // AC2 in the domain: finished, it introduces nothing and has no block that could get done.
    [Fact]
    public void FinishedBonusSession_HasNoDoneBlock_AndIntroducesNothing()
    {
        var session = Session.StartBonus(Guid.NewGuid(), "alice", Goal, Guid.NewGuid(), new DateOnly(2026, 11, 16), A, Now);

        Assert.True(session.Complete(Now.AddMinutes(8)));

        Assert.Empty(session.IntroducedTopicIds());
        Assert.Throws<InvalidOperationException>(session.ToDoneBlock);
        Assert.Equal(session.StartedAt, session.UnitStartedAt([session]));
    }

    private static Dictionary<Guid, TopicStanding> Standings(params (Guid Topic, int Mastery, TopicState? State)[] topics) =>
        topics.ToDictionary(topic => topic.Topic, topic => new TopicStanding(topic.Mastery, topic.State));
}