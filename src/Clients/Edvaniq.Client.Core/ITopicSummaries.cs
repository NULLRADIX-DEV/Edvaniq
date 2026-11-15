namespace Edvaniq.Client.Core;

// The summary of a topic in one length (#346), from Content through the web host.
public interface ITopicSummaries
{
    // Asks for it. Content makes it once: a ready one comes back as it is, a pending one too, a failed one goes again.
    // NotFound for a topic the user doesn't have. NotReady with Content's sentence when there is no material for it yet,
    // Limited with the sentence when the budget is used up, the AI is paused or the topics can't be reached.
    Task<CallOutcome<TopicSummary>> RequestAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken = default);

    // Reads it while it is made. NotFound when it was never asked for.
    Task<CallOutcome<TopicSummary>> GetAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken = default);
}

public enum SummaryLength
{
    Short,
    Standard,
    Detailed,
}

public enum SummaryStatus
{
    Pending,
    Ready,
    Failed,
}

// Text comes once it is ready. FailureReason is Content's sentence for a failed one, if it has one. Sources are for the
// source display that comes later (#351).
public sealed record TopicSummary(
    Guid TopicId,
    SummaryLength Length,
    SummaryStatus Status,
    string? Text,
    string? FailureReason,
    bool AiGenerated,
    IReadOnlyList<SummarySource> Sources);

// Origin as DocumentOrigins, SectionId the first section of the document the summary rests on (#349).
public sealed record SummarySource(
    Guid DocumentId, string DocumentTitle, int? PageFrom, int? PageTo, string? Origin = null, Guid? SectionId = null);