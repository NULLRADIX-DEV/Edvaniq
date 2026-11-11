using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// #222: the newest answer of a topic tells a client its answer has arrived. Always the same one for the same records.
public sealed class LatestAnswerTests
{
    private static readonly DateTime Monday = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Smaller = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Larger = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");

    [Fact]
    public void WithoutRecords_IsNull() => Assert.Null(LearningRecord.Latest([]));

    [Fact]
    public void IsTheNewestByTime_WhateverTheOrder()
    {
        var older = Record(Larger, Monday);
        var newer = Record(Smaller, Monday.AddSeconds(1));

        Assert.Equal(Smaller, LearningRecord.Latest([older, newer]));
        Assert.Equal(Smaller, LearningRecord.Latest([newer, older]));
    }

    // Two answers in the same instant: the larger id, in any order.
    [Fact]
    public void AtTheSameTime_IsAlwaysTheLargerId()
    {
        var first = Record(Smaller, Monday);
        var second = Record(Larger, Monday);

        Assert.Equal(Larger, LearningRecord.Latest([first, second]));
        Assert.Equal(Larger, LearningRecord.Latest([second, first]));
    }

    private static LearningRecord Record(Guid answerId, DateTime at) =>
        LearningRecord.Create(answerId, "alice", Guid.NewGuid(), Guid.NewGuid(), null, true, 2, 4200, null, Mode.Practice, at);
}