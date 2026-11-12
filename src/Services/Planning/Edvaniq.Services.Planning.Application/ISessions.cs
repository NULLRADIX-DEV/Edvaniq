using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The sessions of the current user.
public interface ISessions
{
    Task<Session?> ActiveAsync(CancellationToken cancellationToken);

    // Null for a session that isn't there or isn't the user's.
    Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken);

    // The same, tracked, so SaveAsync stores what changed on it.
    Task<Session?> FindForChangeAsync(Guid id, CancellationToken cancellationToken);

    // The session broken off last on the block, null if there is none.
    Task<Session?> LastAbortedAsync(Guid blockId, CancellationToken cancellationToken);

    // False when the user has an active session already, the database lets only one through.
    Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}