using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Content.UnitTests;

// #326: a long document goes to the AI in parts, and the topics of the parts come back as one list.
public sealed class TopicBatchesTests
{
    [Fact]
    public void DocumentWithinTheLength_IsOnePart() =>
        Assert.Equal([0..3], TopicBatches.Of([1_000, 1_500, 2_000], batchCharacters: 10_000));

    [Fact]
    public void LongDocument_IsCutBetweenSections_NeverInOne() =>
        Assert.Equal([0..2, 2..4, 4..5], TopicBatches.Of([4_000, 4_000, 3_000, 1_000, 5_000], batchCharacters: 8_000));

    // A section longer than a part stands alone, nothing is cut or dropped.
    [Fact]
    public void SectionLongerThanAPart_IsAPartOfItsOwn() =>
        Assert.Equal([0..1, 1..2, 2..3], TopicBatches.Of([1_000, 12_000, 1_000], batchCharacters: 8_000));

    [Fact]
    public void NoSections_NoParts() =>
        Assert.Empty(TopicBatches.Of([], batchCharacters: 8_000));

    // One part keeps the whole limit, so a short document is asked as before.
    [Fact]
    public void OnePart_KeepsEveryTopic() =>
        Assert.Equal([TopicAnalysis.MaxTopics], TopicBatches.Quotas([5_000], TopicAnalysis.MaxTopics));

    [Fact]
    public void Quotas_FollowTheLength_AndStayWithinTheLimit()
    {
        var quotas = TopicBatches.Quotas([40_000, 40_000, 40_000, 40_000, 10_000], 30);

        Assert.Equal([7, 7, 7, 7, 1], quotas);
        Assert.InRange(quotas.Sum(), 1, 30);
    }

    [Fact]
    public void EveryPart_GetsAtLeastOneTopic()
    {
        var quotas = TopicBatches.Quotas([100_000, 10, 10], 6);

        Assert.All(quotas, quota => Assert.True(quota >= 1));
        Assert.InRange(quotas.Sum(), 1, 6);
    }

    // A topic that goes on in the next part, by a name that matches after TopicKey, takes its sections. A subtopic of it
    // under the same topic too, a new one is added.
    [Fact]
    public void TopicGoingOnInTheNextPart_IsOneTopic()
    {
        var s = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        IReadOnlyList<TopicSuggestion> first =
        [
            new("Fotosynthese", null, [s[0]]),
            new("Lichtreaktion", 0, [s[1]]),
        ];
        IReadOnlyList<TopicSuggestion> second =
        [
            new("FOTOSYNTHESE", null, []),
            new("Lichtreaktion", 0, [s[2]]),
            new("Calvin-Zyklus", 0, [s[3]]),
            new("Zellatmung", null, [s[4], s[5]]),
        ];

        var merged = TopicMerge.Merge([first, second], [30, 30], s);

        Assert.Equal(
            [("Fotosynthese", (int?)null, 1), ("Lichtreaktion", 0, 2), ("Calvin-Zyklus", 0, 1), ("Zellatmung", null, 2)],
            merged.Select(topic => (topic.Name, topic.Parent, topic.SectionIds.Count)));
        Assert.Equal([s[1], s[2]], merged[1].SectionIds);
        Assert.Equal(s.Length, merged.Sum(topic => topic.SectionIds.Count));
    }

    // #326: the key terms of a topic that goes on come from both parts, each once.
    [Fact]
    public void TopicGoingOn_TakesTheTermsOfBothParts()
    {
        var s = Enumerable.Range(0, 2).Select(_ => Guid.NewGuid()).ToArray();
        IReadOnlyList<TopicSuggestion> first =
            [new("Fotosynthese", null, [s[0]], [new("Chlorophyll", s[0], "Chlorophyll ist der grüne Farbstoff der Pflanzen.")])];
        IReadOnlyList<TopicSuggestion> second =
        [
            new("Fotosynthese", null, [s[1]],
            [
                new("chlorophyll", s[1], "Chlorophyll fängt das Licht für die Pflanze ein."),
                new("Glucose", s[1], "Glucose ist der Zucker, den die Pflanze bildet."),
            ]),
        ];

        var merged = Assert.Single(TopicMerge.Merge([first, second], [30, 30], s));

        Assert.Equal(["Chlorophyll", "Glucose"], merged.Terms!.Select(term => term.Term));
        Assert.Equal(s, merged.SectionIds);
    }

    // Beyond its quota a part loses topics, never sections: a subtopic's go to its topic, a topic's to the one before.
    [Fact]
    public void TopicsBeyondTheQuota_GiveTheirSectionsAway()
    {
        var s = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        IReadOnlyList<TopicSuggestion> part =
        [
            new("Säuren", null, [s[0]]),
            new("pH-Wert", 0, [s[1]]),
            new("Basen", null, [s[2]]),
            new("Laugen", 2, [s[3]]),
        ];

        var merged = TopicMerge.Merge([part], [1], s);

        var only = Assert.Single(merged);
        Assert.Equal(("Säuren", (int?)null), (only.Name, only.Parent));
        Assert.Equal(s, only.SectionIds);
    }

    [Fact]
    public void SubtopicBeyondTheQuota_GoesToItsTopic()
    {
        var s = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        IReadOnlyList<TopicSuggestion> part =
        [
            new("Säuren", null, [s[0]]),
            new("pH-Wert", 0, [s[1]]),
            new("Indikatoren", 0, [s[2]]),
        ];

        var merged = TopicMerge.Merge([part], [2], s);

        Assert.Equal(["Säuren", "pH-Wert"], merged.Select(topic => topic.Name));
        Assert.Equal([s[0], s[2]], merged[0].SectionIds);
    }
}