using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// The topics an analysis brought, for the handler of DocumentAnalyzed. A consumer has no current user, so the owner
// comes from the event and every query names it itself. Infrastructure implements it.
public interface IAnalyzedTopics
{
    Task<bool> ExistForAsync(string ownerId, Guid documentId, CancellationToken cancellationToken);

    // Only adds them to the context, the consumer saves them together with its inbox entry.
    void Add(TopicImport import);
}