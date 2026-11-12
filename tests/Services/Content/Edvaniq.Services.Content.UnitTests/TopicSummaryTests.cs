using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #344: a summary waits for the worker, gets ready once, and only a failed one goes back to it.
public sealed class TopicSummaryTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Request_KeepsTheMaterialInItsOrder_AndWaits()
    {
        var (first, second) = ((Guid.NewGuid(), Guid.NewGuid()), (Guid.NewGuid(), Guid.NewGuid()));

        var summary = TopicSummary.Request("alice", Guid.NewGuid(), null, SummaryLength.Standard, [first, second], Now);

        Assert.Equal(SummaryStatus.Pending, summary.Status);
        Assert.Equal([first, second], summary.Sections.Select(section => (section.DocumentId, section.SectionId)));
        Assert.Equal([0, 1], summary.Sections.Select(section => section.Position));
        Assert.All(summary.Sections, section => Assert.Equal(("alice", summary.Id), (section.OwnerId, section.SummaryId)));
    }

    [Fact]
    public void Request_WithoutMaterial_IsRefused() =>
        Assert.Throws<ArgumentException>(() => TopicSummary.Request("alice", Guid.NewGuid(), null, SummaryLength.Short, [], Now));

    [Fact]
    public void Finish_MarksTheUsedSections_AndOnlyOnce()
    {
        var summary = Summary(3);

        summary.Finish("Text", [0, 2], "model", "summary-1", Now);

        Assert.Equal((SummaryStatus.Ready, "Text", "model", "summary-1", (DateTime?)Now),
            (summary.Status, summary.Text, summary.Model, summary.PromptVersion, summary.ReadyAt));
        Assert.Equal([true, false, true], summary.Sections.Select(section => section.Used));
        Assert.Throws<InvalidOperationException>(() => summary.Finish("Anders", [1], "model", "summary-1", Now));
        Assert.False(summary.Retry());
    }

    [Fact]
    public void Failed_GoesBackToTheWorker()
    {
        var summary = Summary(1);
        summary.Fail(FailureCode.AiUnavailable);

        Assert.True(summary.Retry());

        Assert.Equal((SummaryStatus.Pending, (FailureCode?)null), (summary.Status, summary.FailureCode));
    }

    [Fact]
    public void Postpone_WaitsAfterEachOutage_UntilTheDelaysAreUsedUp()
    {
        var summary = Summary(1);
        TimeSpan[] delays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

        Assert.True(summary.Postpone(Now, delays, null));
        Assert.Equal(Now.AddSeconds(30), summary.NextAttemptAt);
        Assert.True(summary.Postpone(Now, delays, TimeSpan.FromHours(1)));
        Assert.Equal(Now + Document.MaxOutageDelay, summary.NextAttemptAt);
        Assert.False(summary.Postpone(Now, delays, null));
    }

    private static TopicSummary Summary(int sections) =>
        TopicSummary.Request(
            "alice", Guid.NewGuid(), null, SummaryLength.Short,
            [.. Enumerable.Range(0, sections).Select(_ => (Guid.NewGuid(), Guid.NewGuid()))], Now);
}