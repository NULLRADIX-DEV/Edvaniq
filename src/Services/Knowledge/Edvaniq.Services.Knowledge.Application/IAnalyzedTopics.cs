using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// The topics an analysis brought, for the handler of DocumentAnalyzed and for Content asking which ids they got. Neither
// has a current user, so the owner comes from the event or the document, and every query names it itself.
// Infrastructure implements it.
public interface IAnalyzedTopics
{
    // Adds the new topics and makes the import's sections the only ones of its document. Only changes the context, the
    // consumer saves it together with its inbox entry.
    Task ImportAsync(TopicImport import, CancellationToken cancellationToken);

    // The user's topics of the learning goal, tracked, so a change goes out with the next save.
    Task<IReadOnlyList<Topic>> OfGoalAsync(string ownerId, Guid learningGoalId, CancellationToken cancellationToken);

    // The topics the analysis of the document brought or hung sections on, in the order of the analysis that made them,
    // each with its sections of this document. A document belongs to one user, so these are that user's topics, whoever
    // asks.
    Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}

// Position is the place of the suggestion in the DocumentAnalyzed that made the topic, SectionIds are Content's sections
// of the topic in the document asked for.
public sealed record DocumentTopic(Guid Id, Guid? ParentId, int Position, IReadOnlyList<Guid> SectionIds);