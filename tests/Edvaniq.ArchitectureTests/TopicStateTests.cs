namespace Edvaniq.ArchitectureTests;

// docs/architecture.md, rule 6: only LearningEngine knows the thresholds of the topic states, every other service and
// the clients take the state over (#367).
public class TopicStateTests
{
    [Fact]
    public void OnlyLearningEngine_HoldsTheThresholdsOfTheTopicStates() =>
        Rules.AssertNone(TopicStateRule.Violations(Repository.Root), "The topic state rule (rule 6)");

    [Fact]
    public void ThresholdNames_ComeFromTopicStates() =>
        Assert.Contains("WeakBelow", TopicStateRule.ThresholdNames);

    [Fact]
    public void Thresholds_AreReported()
    {
        string[] lines =
        [
            "if (mastery < 50)",
            "return topic.Mastery >= SecureFrom ? Secure : Building;",
            "if (mastery>=80)",
            "var weak = 50 > masteryOfTopic;",
            "var weak = MasteryOf(id, masteries) < Weak;",
            "if (limit <= standing.Mastery)",
            "if (mastery is < 50 or > 80)",
            "if (standing is { Mastery: < 50 })",
            "var state = mastery switch",
            "private const int WeakBelow = 50;",
            "Edvaniq.Services.LearningEngine.Domain.TopicStates.Of(true, mastery, records)",
        ];

        Assert.Equal(
            [
                "Plan.cs:1 compares a mastery",
                "Plan.cs:2 names SecureFrom",
                "Plan.cs:2 compares a mastery",
                "Plan.cs:3 compares a mastery",
                "Plan.cs:4 compares a mastery",
                "Plan.cs:5 compares a mastery",
                "Plan.cs:6 compares a mastery",
                "Plan.cs:7 compares a mastery",
                "Plan.cs:8 compares a mastery",
                "Plan.cs:9 compares a mastery",
                "Plan.cs:10 names WeakBelow",
                "Plan.cs:11 names TopicStates",
            ],
            TopicStateRule.Check("Plan.cs", lines));
    }

    [Fact]
    public void WorkingWithTheMastery_IsNoThreshold()
    {
        string[] lines =
        [
            "new Weighted(topic.TopicId, (long)topic.Size * (100 - topic.Mastery), topic.IsNew)",
            "Math.Clamp(standing.Mastery, 0, 100)",
            "ArgumentOutOfRangeException.ThrowIfGreaterThan(topic.Mastery, 100, nameof(topics));",
            "Task<IReadOnlyDictionary<Guid, TopicMastery>?> OfLearningGoalAsync(Guid id);",
            "private sealed record MasteryList(int RuleVersion, IReadOnlyList<TopicMastery> Topics);",
            "IsNew: standing.State == TopicState.New",
            "MasteryAvailable: topics.Count == 0 || mastery is not null",
            "masteries.ToDictionary(topic => topic.TopicId, topic => topic.Mastery)",
            "if (difficulty is < 1 or > 3)",
        ];

        Assert.Empty(TopicStateRule.Check("Plan.cs", lines));
    }

    // The real rule of LearningEngine would be caught anywhere else.
    [Fact]
    public void TopicStatesOfLearningEngine_WouldBeReportedOutsideIt()
    {
        var file = Path.Combine(
            Repository.Root, "src", "Services", "LearningEngine", "Edvaniq.Services.LearningEngine.Domain", "TopicStates.cs");

        var found = TopicStateRule.Check("src/Services/Planning/Copy.cs", File.ReadAllLines(file)).ToList();

        Assert.Contains(found, violation => violation.EndsWith("names TopicStates", StringComparison.Ordinal));
        Assert.Contains(found, violation => violation.EndsWith("compares a mastery", StringComparison.Ordinal));
    }
}