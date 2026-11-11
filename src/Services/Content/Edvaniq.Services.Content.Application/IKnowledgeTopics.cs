namespace Edvaniq.Services.Content.Application;

// Asks Knowledge which ids the topics of an analysis got, a query by rule 6: Knowledge listens to Content, so Content
// never listens to Knowledge. Infrastructure implements it with the worker's own token.
public interface IKnowledgeTopics
{
    // The topics in the order of the analysis, empty while Knowledge has none of the document. Throws if Knowledge can't
    // be asked.
    Task<IReadOnlyList<KnowledgeTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken);
}

// Position is the place of the suggestion in DocumentAnalyzed, SectionIds are the document's sections of the topic.
public sealed record KnowledgeTopic(Guid Id, Guid? ParentId, int Position, IReadOnlyList<Guid> SectionIds);