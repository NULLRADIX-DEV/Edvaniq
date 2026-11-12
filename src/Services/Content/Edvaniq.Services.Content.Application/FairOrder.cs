namespace Edvaniq.Services.Content.Application;

// The order in which a worker takes the waiting documents: users take turns. First the oldest document of every user,
// then the second of every user, and so on, each turn oldest first. One user with fifty documents waits behind the first
// document of everyone else, and a single user still gets all of theirs in a row.
public static class FairOrder
{
    // How many of the oldest waiting documents a round looks at besides the oldest of every user.
    public const int MaxCandidates = 500;

    public static IReadOnlyList<Guid> Of(IEnumerable<WaitingDocument> documents, int take) =>
        [.. documents
            .DistinctBy(document => document.Id)
            .GroupBy(document => document.OwnerId, StringComparer.Ordinal)
            .SelectMany(owner => owner
                .OrderBy(document => document.Since)
                .ThenBy(document => document.Id)
                .Select((document, turn) => (document, turn)))
            .OrderBy(item => item.turn)
            .ThenBy(item => item.document.Since)
            .ThenBy(item => item.document.Id)
            .Select(item => item.document.Id)
            .Take(take)];
}

// A document a worker could take on. Since is what it queues by, its age or the time it is due again after an outage.
public sealed record WaitingDocument(Guid Id, string OwnerId, DateTime Since);