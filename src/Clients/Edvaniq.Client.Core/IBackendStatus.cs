namespace Edvaniq.Client.Core;

// Whether the client gets through to the backend right now. The shell asks before anything else, so a user sees a
// hint with a way to try again instead of pages that silently stay empty.
public interface IBackendStatus
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);
}