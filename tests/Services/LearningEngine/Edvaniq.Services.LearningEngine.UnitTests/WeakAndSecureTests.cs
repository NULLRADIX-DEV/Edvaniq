using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// #453: the weaknesses the worst first and the secure topics, from the topic states; reported items count for nothing.
public sealed class WeakAndSecureTests
{
    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = new("00000000-0000-0000-0000-00000000000d");
    private static readonly Guid E = new("00000000-0000-0000-0000-00000000000e");
    private static readonly Guid F = new("00000000-0000-0000-0000-00000000000f");

    // AC1: the lowest mastery is the worst; a weakness only by its error rate has the higher mastery and comes after;
    // ties by topic id.
    [Fact]
    public void Weaknesses_TheLowestMasteryFirst_ThenByTopicId()
    {
        var lists = WeakAndSecure.Of(
        [
            (A, new TopicStanding(70, TopicState.Weakness)),
            (B, new TopicStanding(30, TopicState.Weakness)),
            (C, new TopicStanding(12, TopicState.Weakness)),
            (D, new TopicStanding(30, TopicState.Weakness)),
        ]);

        Assert.Equal(
            [
                new WeakTopic(C, 12, WeaknessReason.Mastery),
                new WeakTopic(B, 30, WeaknessReason.Mastery),
                new WeakTopic(D, 30, WeaknessReason.Mastery),
                new WeakTopic(A, 70, WeaknessReason.RecentErrors),
            ],
            lists.Weaknesses);
    }

    // Secure topics the highest first, new and building ones in neither list.
    [Fact]
    public void SecureTopics_TheHighestFirst_NewAndBuildingInNeither()
    {
        var lists = WeakAndSecure.Of(
        [
            (A, new TopicStanding(80, TopicState.Secure)),
            (B, new TopicStanding(95, TopicState.Secure)),
            (C, new TopicStanding(50, TopicState.New)),
            (D, new TopicStanding(65, TopicState.Building)),
            (E, new TopicStanding(80, TopicState.Secure)),
            (F, new TopicStanding(49, TopicState.Weakness)),
        ]);

        Assert.Equal([new SecureTopic(B, 95), new SecureTopic(A, 80), new SecureTopic(E, 80)], lists.Secure);
        Assert.Equal([F], lists.Weaknesses.Select(topic => topic.TopicId));
    }

    [Fact]
    public void NoTopics_GiveTwoEmptyLists()
    {
        var lists = WeakAndSecure.Of([]);

        Assert.Empty(lists.Weaknesses);
        Assert.Empty(lists.Secure);
    }

    // From the records as TopicStates sees them: the carry of expired records counts as well (#365).
    [Fact]
    public void Standings_FromRecordsAndCarry()
    {
        var gone = Enumerable.Range(0, 4).Select(minute => Answer(false, Monday.AddMinutes(minute))).ToList();
        var carry = MasteryCarry.Fold(null, "alice", A, gone, null);
        var now = Monday.AddDays(1);

        var lists = WeakAndSecure.Of([(A, TopicStates.Assess([], null, now, carry: carry))]);

        Assert.Equal(TopicStates.Assess(gone, null, now).Mastery, Assert.Single(lists.Weaknesses).Mastery);
    }

    // AC3: an answer on a reported item counts for nothing. With it the topic is a weakness by its error rate, without
    // it the same topic is secure.
    [Fact]
    public void ExcludedItem_CountsNeitherForMasteryNorForTheErrorRate()
    {
        var reported = Guid.NewGuid();
        LearningRecord[] records =
        [
            Answer(true, Monday, difficulty: 3), Answer(true, Monday.AddMinutes(1), difficulty: 3),
            Answer(true, Monday.AddMinutes(2), difficulty: 3), Answer(true, Monday.AddMinutes(3), difficulty: 3),
            Answer(false, Monday.AddMinutes(4), item: reported), Answer(false, Monday.AddMinutes(5), item: reported),
        ];
        var now = Monday.AddMinutes(6);
        var excluded = new HashSet<Guid> { reported };

        var counted = TopicStates.Assess(records, null, now);
        var without = TopicStates.Assess(records, null, now, excludedItemIds: excluded);

        Assert.Equal(TopicState.Weakness, counted.State);
        Assert.Equal(TopicStates.Assess(records[..4], null, now), without);
        Assert.Equal(TopicState.Secure, without.State);
    }

    // Only answers on reported items: nothing counts, so the topic isn't introduced by them either.
    [Fact]
    public void OnlyExcludedItems_LeaveTheTopicNew()
    {
        var reported = Guid.NewGuid();
        var standing = TopicStates.Assess([Answer(false, Monday, item: reported)], null, Monday, excludedItemIds: new HashSet<Guid> { reported });

        Assert.Equal(new TopicStanding(50, TopicState.New), standing);
    }

    // Decayed leaves the excluded answers out the same way.
    [Fact]
    public void Decayed_LeavesExcludedItemsOut()
    {
        var reported = Guid.NewGuid();
        LearningRecord[] secure = [.. Enumerable.Range(0, 4).Select(minute => Answer(true, Monday.AddMinutes(minute)))];
        LearningRecord[] records = [.. secure, Answer(false, Monday.AddMinutes(4), item: reported)];
        var now = Monday.AddDays(30);

        Assert.Null(TopicStates.Decayed(records, null, now));
        Assert.Equal(TopicStates.Decayed(secure, null, now), TopicStates.Decayed(records, null, now, excludedItemIds: new HashSet<Guid> { reported }));
        Assert.NotNull(TopicStates.Decayed(secure, null, now));
    }

    private static LearningRecord Answer(bool correct, DateTime answeredAt, int difficulty = 2, Guid? item = null) =>
        LearningRecord.Create(Guid.NewGuid(), "alice", A, item ?? Guid.NewGuid(), null, correct, difficulty, 3000, null, Mode.Practice, answeredAt);
}