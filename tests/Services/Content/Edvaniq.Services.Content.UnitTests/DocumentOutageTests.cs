using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// An outage of the AI postpones a document by the next delay, longer only if the provider asks for it and never
// beyond the cap. After the last delay it fails with a reason the user can act on, and a retry starts afresh.
public class DocumentOutageTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan[] Delays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    [Fact]
    public void Outages_WaitTheDelaysInTurn_ThenTheDocumentFails()
    {
        var document = Analyzing();

        Assert.True(document.Postpone(Now, Delays, null));
        Assert.Equal((1, (DateTime?)Now.AddSeconds(30)), (document.AiOutages, document.NextAttemptAt));
        Assert.True(document.Postpone(Now, Delays, null));
        Assert.Equal((2, (DateTime?)Now.AddMinutes(2)), (document.AiOutages, document.NextAttemptAt));

        Assert.False(document.Postpone(Now, Delays, null));

        Assert.Equal((DocumentStatus.Failed, "KI derzeit nicht verfügbar. Versuch es später noch einmal."), (document.Status, document.FailureReason));
    }

    [Fact]
    public void DefaultDelays_EndAfterAFewMinutes_NotNever()
    {
        Assert.InRange(Document.OutageDelays.Count, 1, 5);
        Assert.All(Document.OutageDelays, delay => Assert.InRange(delay, TimeSpan.FromSeconds(1), Document.MaxOutageDelay));
    }

    [Theory]
    [InlineData(10, 30)]
    [InlineData(300, 300)]
    [InlineData(7200, 900)]
    public void RetryAfter_CountsWhenLonger_UpToTheCap(int retryAfterSeconds, int expectedSeconds)
    {
        var document = Analyzing();

        document.Postpone(Now, Delays, TimeSpan.FromSeconds(retryAfterSeconds));

        Assert.Equal(Now.AddSeconds(expectedSeconds), document.NextAttemptAt);
    }

    [Fact]
    public void Retry_AndReportedTopics_StartTheOutagesAfresh()
    {
        var retried = Analyzing();
        retried.Postpone(Now, Delays, null);
        retried.Postpone(Now, Delays, null);
        retried.Postpone(Now, Delays, null);
        retried.Retry(Now.AddHours(1));
        var reported = Analyzing();
        reported.Postpone(Now, Delays, null);
        reported.ReportTopics(Now.AddMinutes(1));

        Assert.Equal((0, (DateTime?)null), (retried.AiOutages, retried.NextAttemptAt));
        Assert.Equal((0, (DateTime?)null), (reported.AiOutages, reported.NextAttemptAt));
    }

    [Fact]
    public void OnlyADocumentInAnalysis_CanWait() =>
        Assert.Throws<InvalidOperationException>(() =>
            Document.Paste("alice", Guid.NewGuid(), null, "Licht spaltet Wasser.", Now).Postpone(Now, Delays, null));

    private static Document Analyzing()
    {
        var document = Document.Paste("alice", Guid.NewGuid(), null, "Licht spaltet Wasser.", Now);
        document.Split();
        return document;
    }
}