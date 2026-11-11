using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// The sums of deleted users: per UTC day, purpose, model and prompt version, a call counts on the day it started.
public class AiUsageDayTests
{
    private static readonly Document Material = Document.Paste("alice", Guid.NewGuid(), null, "Licht spaltet Wasser.", new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Attempts_AddUp_ACallCountsOnce_AndAMissingCostIsCounted()
    {
        var call = Guid.NewGuid();
        var first = Attempt(call, 1, At(23, 59), 0.002m);
        var sum = AiUsageDay.For(first);

        sum.Add(first);
        sum.Add(Attempt(call, 2, At(23, 59), null));

        Assert.Equal(
            (new DateOnly(2026, 11, 11), 1, 2, 20L, 6L, 0.002m, 1, 3000L),
            (sum.Day, sum.Calls, sum.Attempts, sum.InputTokens, sum.OutputTokens, sum.Cost, sum.UnpricedAttempts, sum.DurationMs));
    }

    [Fact]
    public void AttemptOfAnotherDayOrModel_DoesNotFit()
    {
        var sum = AiUsageDay.For(Attempt(Guid.NewGuid(), 1, At(10, 0), 0m));

        Assert.False(sum.Holds(Attempt(Guid.NewGuid(), 1, At(10, 0).AddDays(1), 0m)));
        Assert.False(sum.Holds(Attempt(Guid.NewGuid(), 1, At(10, 0), 0m, model: "other")));
        Assert.Throws<InvalidOperationException>(() => sum.Add(Attempt(Guid.NewGuid(), 1, At(10, 0), 0m, model: "other")));
    }

    [Fact]
    public void LongModelName_IsCut_NotRefused() =>
        Assert.Equal(AiAttempt.ModelMaxLength, Attempt(Guid.NewGuid(), 1, At(10, 0), 0m, model: new string('m', 300)).Model.Length);

    private static DateTime At(int hour, int minute) => new(2026, 11, 11, hour, minute, 0, DateTimeKind.Utc);

    private static AiAttempt Attempt(Guid call, int number, DateTime at, decimal? cost, string model = "vendor/model-1") =>
        AiAttempt.Of(call, number, Material, AiAttempt.Topics, model, "topics-1", at, TimeSpan.FromMilliseconds(1500), 10, 3, cost, AiAttemptOutcome.Answered);
}