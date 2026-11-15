using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Testing;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.LearningEngine.UnitTests;

// #417: answers still missing well after a session ended give a warning, before that they may just be on their way.
public sealed class AnswersLateTests
{
    private static readonly DateTime Ended = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly TestLogs logs = new();

    [Theory]
    [InlineData(2, 6, true)]
    [InlineData(2, 5, false)]
    [InlineData(0, 30, false)]
    public void MissingAnswers_AreWorthAWarning_OnlyWhenLate(int missing, int minutesAfter, bool warned)
    {
        LearningEngineLog.IfAnswersLate(Logger(), missing, 10, Ended, Ended.AddMinutes(minutesAfter));

        if (!warned)
        {
            Assert.Empty(logs.Entries);
            return;
        }

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(("MasteryChangesAnswersLate", LogLevel.Warning), (entry.EventName, entry.Level));
        Assert.Equal($"Mastery changes asked with {missing} of 10 answers missing, {minutesAfter} min after the session ended", entry.Message);
    }

    // The limit Planning's result waits for (#406).
    [Fact]
    public void Limit_IsFiveMinutes() => Assert.Equal(TimeSpan.FromMinutes(5), LearningEngineLog.AnswersLateAfter);

    private ILogger Logger()
    {
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        return factory.CreateLogger("test");
    }
}