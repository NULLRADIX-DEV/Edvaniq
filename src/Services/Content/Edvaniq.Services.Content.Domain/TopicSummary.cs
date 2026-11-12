using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// The summary of one topic in one length (#344), made once from the sections Knowledge named for the topic when it was
// asked for, and kept. The Api takes the request, the worker asks the AI, the next read costs nothing.
public sealed class TopicSummary : IOwnedByUser
{
    private readonly List<TopicSummarySection> sections = [];

    private TopicSummary(string ownerId, Guid topicId, Guid? learningGoalId, SummaryLength length, DateTime createdAt)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        LearningGoalId = learningGoalId;
        Length = length;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    // Knowledge's topic.
    public Guid TopicId { get; private set; }

    // Of the material's documents, for the profile of the prompt. Null for documents from before learning goals.
    public Guid? LearningGoalId { get; private set; }

    public SummaryLength Length { get; private set; }

    public SummaryStatus Status { get; private set; } = SummaryStatus.Pending;

    // Set once the summary is ready.
    public string? Text { get; private set; }

    public string? Model { get; private set; }

    public string? PromptVersion { get; private set; }

    // Set while failed.
    public FailureCode? FailureCode { get; private set; }


    // UTC.
    public DateTime CreatedAt { get; private set; }

    public DateTime? ReadyAt { get; private set; }

    // How often the AI was away for it in a row, and when the worker may ask again (Document.OutageDelays).
    public int AiOutages { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    // The material, in the order of the prompt.
    public IReadOnlyList<TopicSummarySection> Sections => sections;

    public static TopicSummary Request(
        string ownerId, Guid topicId, Guid? learningGoalId, SummaryLength length, IReadOnlyList<(Guid DocumentId, Guid SectionId)> material,
        DateTime now)
    {
        if (material.Count == 0)
        {
            throw new ArgumentException("A summary needs material.", nameof(material));
        }

        var summary = new TopicSummary(ownerId, topicId, learningGoalId, length, now);
        summary.sections.AddRange(material.Select((item, position) =>
            new TopicSummarySection(summary.Id, ownerId, item.DocumentId, item.SectionId, position)));
        return summary;
    }

    // The text with the sections it rests on, given by their place in the material.
    public void Finish(string text, IReadOnlyCollection<int> usedPositions, string model, string promptVersion, DateTime now)
    {
        if (Status != SummaryStatus.Pending)
        {
            throw new InvalidOperationException($"Summary {Id} is {Status}, only a pending one can finish.");
        }

        foreach (var section in sections)
        {
            section.MarkUsed(usedPositions.Contains(section.Position));
        }

        Status = SummaryStatus.Ready;
        Text = text;
        Model = model;
        PromptVersion = promptVersion;
        ReadyAt = now;
        ClearOutages();
    }

    public void Fail(FailureCode code)
    {
        Status = SummaryStatus.Failed;
        FailureCode = code;
        ClearOutages();
    }

    // A failed summary is asked for again: it goes back to the worker with the same material. A ready one stays.
    public bool Retry()
    {
        if (Status != SummaryStatus.Failed)
        {
            return false;
        }

        Status = SummaryStatus.Pending;
        FailureCode = null;
        return true;
    }

    // Like Document.Postpone: false once the delays are used up, then it should fail.
    public bool Postpone(DateTime now, IReadOnlyList<TimeSpan> delays, TimeSpan? retryAfter)
    {
        if (AiOutages >= delays.Count)
        {
            return false;
        }

        var delay = delays[AiOutages];
        if (retryAfter > delay)
        {
            delay = retryAfter < Document.MaxOutageDelay ? retryAfter.Value : Document.MaxOutageDelay;
        }

        AiOutages++;
        NextAttemptAt = now + delay;
        return true;
    }

    private void ClearOutages()
    {
        AiOutages = 0;
        NextAttemptAt = null;
    }
}

// One section of a summary's material. Used says whether the AI names it as a ground of the text, those are the sources.
public sealed class TopicSummarySection : IOwnedByUser
{
    internal TopicSummarySection(Guid summaryId, string ownerId, Guid documentId, Guid sectionId, int position)
    {
        SummaryId = summaryId;
        OwnerId = ownerId;
        DocumentId = documentId;
        SectionId = sectionId;
        Position = position;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid SummaryId { get; private set; }

    public string OwnerId { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid SectionId { get; private set; }

    // From 0, the order of the prompt.
    public int Position { get; private set; }

    public bool Used { get; private set; }

    internal void MarkUsed(bool used) => Used = used;
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