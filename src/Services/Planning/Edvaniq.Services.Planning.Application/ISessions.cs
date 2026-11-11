using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The sessions of the current user.
public interface ISessions
{
    Task<Session?> ActiveAsync(CancellationToken cancellationToken);

    // Null for a session that isn't there or isn't the user's.
    Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken);

    // False when the user has an active session already, the database lets only one through.
    Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken);
}