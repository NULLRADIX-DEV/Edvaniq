using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The answers given on a block and in its sessions, asked from Assessment (#403). Infrastructure implements it with the
// caller's own token, so only their answers come back.
public interface ISessionAnswers
{
    // Null when Assessment can't be asked.
    Task<IReadOnlyList<SessionAnswer>?> OfBlockAsync(Guid blockId, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);
}