using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Breaking a session off and moving it on (#393). The answers given so far are with Assessment and LearningEngine
// already, so breaking off only ends the session here: the block stays open and counts as started (#407).
public sealed class SessionChanges(ISessions sessions, TimeProvider time)
{
    // Null for a session that isn't there or isn't the user's. A session that ended already comes back as it is.
    public async Task<Session?> AbortAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await sessions.FindForChangeAsync(id, cancellationToken) is not { } session)
        {
            return null;
        }

        if (session.Abort(time.GetUtcNow().UtcDateTime))
        {
            await sessions.SaveAsync(cancellationToken);
        }

        return session;
    }

    public async Task<(SessionAdvance? Result, Session? Session)> AdvanceAsync(Guid id, int step, CancellationToken cancellationToken)
    {
        if (await sessions.FindForChangeAsync(id, cancellationToken) is not { } session)
        {
            return (null, null);
        }

        var result = session.Advance(step);
        if (result == SessionAdvance.Advanced)
        {
            await sessions.SaveAsync(cancellationToken);
        }

        return (result, session);
    }
}