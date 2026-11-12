using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #377: the fixed steps of a session.
public class SessionStepsTests
{
    private static readonly SessionStep EmptyReview = new(SessionStepKind.Review, Empty: true);
    private static readonly SessionStep Unit = new(SessionStepKind.IntroductionUnit);
    private static readonly SessionStep Quiz = new(SessionStepKind.Quiz);
    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewTopic_GetsTheUnitBeforeTheQuiz() =>
        Assert.Equal(new[] { EmptyReview, Unit, Quiz }, SessionSteps.For(BlockKind.Learn, topicIsNew: true));

    [Fact]
    public void KnownTopic_GetsOnlyTheQuiz() =>
        Assert.Equal(new[] { EmptyReview, Quiz }, SessionSteps.For(BlockKind.Learn, topicIsNew: false));

    // A review block never introduces a topic, even one LearningEngine calls new.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviewBlock_GetsOnlyTheQuiz(bool topicIsNew) =>
        Assert.Equal(new[] { EmptyReview, Quiz }, SessionSteps.For(BlockKind.Review, topicIsNew));

    [Fact]
    public void Session_KeepsTheStepsOfItsStart()
    {
        var block = DayBlocks.WithIds("alice", Goal, Today, [new Block(Topic, 15)])[0];

        Assert.Equal(new[] { EmptyReview, Unit, Quiz }, Session.Start(Guid.NewGuid(), "alice", Goal, Today, block, Now, topicIsNew: true).Steps);
        Assert.Equal(new[] { EmptyReview, Quiz }, Session.Start(Guid.NewGuid(), "alice", Goal, Today, block, Now, topicIsNew: false).Steps);
    }

    // The same rule as the planner: new when LearningEngine says so or doesn't know the topic, a state Planning doesn't
    // know is not new.
    [Theory]
    [InlineData("new", true)]
    [InlineData("building", false)]
    [InlineData("weakness", false)]
    [InlineData("secure", false)]
    [InlineData(null, false)]
    public void IsNew_FollowsLearningEngine(string? state, bool isNew)
    {
        var standings = new Dictionary<Guid, TopicStanding> { [Topic] = new(50, ParseState(state)) };

        Assert.Equal(isNew, TodayPlanner.IsNew(Topic, standings));
    }

    [Fact]
    public void IsNew_ForATopicLearningEngineDoesNotKnow() =>
        Assert.True(TodayPlanner.IsNew(Topic, new Dictionary<Guid, TopicStanding>()));

    // The integration tests start a learn block of a known topic: one with mastery 20 gets one on a day of 30 minutes.
    [Fact]
    public void KnownWeakTopic_HasALearnBlockToday()
    {
        var plan = TodayPlanner.Plan([Topic], new Dictionary<Guid, TopicStanding> { [Topic] = new(20, TopicState.Building) }, DailyBudget.Uniform(30), Now);

        Assert.Contains(plan.Blocks, block => block.Kind == BlockKind.Learn);
    }

    private static TopicState? ParseState(string? state) => state is null ? null : Enum.Parse<TopicState>(state, ignoreCase: true);
}