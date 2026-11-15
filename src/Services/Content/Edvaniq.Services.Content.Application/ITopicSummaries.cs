using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// Asks Knowledge which sections are the material of a topic (#294), a query by rule 6 like ILearningGoals: Knowledge
// listens to Content, so Content never listens to Knowledge. Infrastructure implements it with the user's own token,
// so Knowledge only answers for that user's topics.
public interface ITopicMaterial
{
    // The sections of the topic, null if the user has no such topic. Throws TopicMaterialUnavailableException if
    // Knowledge can't be asked.
    Task<IReadOnlyList<(Guid DocumentId, Guid SectionId)>?> SectionsAsync(Guid topicId, CancellationToken cancellationToken);
}

public sealed class TopicMaterialUnavailableException(Exception innerException)
    : Exception("Knowledge could not be asked for the sections of the topic.", innerException);

// The summaries of the current user (#344). Infrastructure implements it, the owner filter keeps others' out.
public interface ITopicSummaries
{
    Task<SummaryView?> FindAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken);

    // The summary of the topic in this length: the stored one, a failed one asked for again, or a new request for the
    // worker with the material Knowledge names now. The summary is null unless the result is Created, Existing or Retried.
    // ai says whether the AI may take a new job. A paused AI or a used up budget turns a new or retried summary away,
    // unless all its material is the demo, which needs no AI (#413).
    Task<(SummaryRequestResult Result, SummaryView? Summary)> RequestAsync(
        Guid topicId, SummaryLength length, AiState ai, CancellationToken cancellationToken);
}

public enum SummaryRequestResult
{
    Created,
    Existing,
    Retried,

    // The user has no such topic.
    TopicNotFound,

    // The topic has no section of the user's documents, so there is nothing to sum up.
    NoMaterial,

    // A new or retried summary of material that needs the AI, while it is paused or out of budget.
    AiPaused,
    BudgetUsedUp,
}

// Whether the AI takes new jobs, as the Api found it before the request.
public enum AiState
{
    Running,
    Paused,
    BudgetUsedUp,
}

// A summary with its sources: the documents and pages of the sections it rests on, one entry per document.
public sealed record SummaryView(TopicSummary Summary, IReadOnlyList<SummarySource> Sources);

// SectionId is the first section of the document the summary rests on, in the order of the document.
public sealed record SummarySource(Guid DocumentId, string DocumentTitle, int? PageFrom, int? PageTo, DocumentOrigin Origin, Guid SectionId);