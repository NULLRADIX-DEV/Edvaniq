namespace Edvaniq.Client.Core;

// The topic directory (#294): all topics with the hidden ones, and per topic its key terms and the sections it comes from.
// Knowledge knows which sections belong to a topic, only Content knows where they stand in a document.
public interface ITopicDirectory
{
    // Both levels, hidden ones included and marked.
    Task<CallOutcome<IReadOnlyList<TopicInfo>>> ListTopicsAsync(CancellationToken cancellationToken = default);

    // The user's documents, newest first, for their titles.
    Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    Task<CallOutcome<IReadOnlyList<KeyTermInfo>>> TermsAsync(Guid topicId, CancellationToken cancellationToken = default);

    Task<CallOutcome<IReadOnlyList<TopicSectionInfo>>> TopicSectionsAsync(Guid topicId, CancellationToken cancellationToken = default);

    // NotFound for a document that is gone.
    Task<CallOutcome<IReadOnlyList<DocumentSectionInfo>>> DocumentSectionsAsync(Guid documentId, CancellationToken cancellationToken = default);
}

// A term and its definition as the analysis took it from the material.
public sealed record KeyTermInfo(string Term, string Definition, Guid? DocumentId = null, Guid? SectionId = null);

// A section of the document DocumentId that is material of the topic.
public sealed record TopicSectionInfo(Guid DocumentId, Guid SectionId);

// Where a section stands: Position counts from 0, the pages only for a file.
public sealed record DocumentSectionInfo(Guid SectionId, int Position, int? PageFrom = null, int? PageTo = null);