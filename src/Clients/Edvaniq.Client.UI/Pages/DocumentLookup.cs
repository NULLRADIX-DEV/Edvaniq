using Edvaniq.Client.Core;

namespace Edvaniq.Client.UI.Pages;

// The user's documents for the titles and origins of sources (#351), asked once per page and kept. A call that didn't
// get through is asked again next time.
public sealed class DocumentLookup(ILearningMaterial material)
{
    private Task<CallOutcome<IReadOnlyList<DocumentInfo>>>? documents;

    public async Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListAsync(CancellationToken cancellationToken)
    {
        documents ??= material.ListDocumentsAsync(cancellationToken);
        var outcome = await documents;
        if (outcome.Result != CallResult.Ok)
        {
            documents = null;
        }

        return outcome;
    }
}