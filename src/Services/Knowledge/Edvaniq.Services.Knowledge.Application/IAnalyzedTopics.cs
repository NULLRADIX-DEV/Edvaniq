using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// The topics an analysis brought, for the handler of DocumentAnalyzed and for Content asking which ids they got. Neither
// has a current user, so the owner comes from the event or the document, and every query names it itself.
// Infrastructure implements it.
public interface IAnalyzedTopics
{
    Task<bool> ExistForAsync(string ownerId, Guid documentId, CancellationToken cancellationToken);

    // Only adds them to the context, the consumer saves them together with its inbox entry.
    void Add(TopicImport import);

    // The topics that came from the analysis of the document, in the order of the analysis. A document belongs to one
    // user, so these are that user's topics, whoever asks.
    Task<IReadOnlyList<DocumentTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}

// Position is the place of the suggestion in DocumentAnalyzed, SectionIds are Content's sections of the topic.
public sealed record DocumentTopic(Guid Id, Guid? ParentId, int Position, IReadOnlyList<Guid> SectionIds);