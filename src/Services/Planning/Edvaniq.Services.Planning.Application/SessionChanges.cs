using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// Breaking a session off, moving it on (#393) and the user's activity in it (#394). The answers given so far are with
// Assessment and LearningEngine already, so breaking off only ends the session here: the block stays open and counts as
// started (#407). Every change is a signal for the active learning time, its moment comes from the server's clock.
public sealed class SessionChanges(ISessions sessions, TimeProvider time)
{
    // Two signals at once are normal (a heartbeat while the user moves on). The one stored second goes again on the
    // session as stored, a few times at most.
    private const int Attempts = 3;

    // Null for a session that isn't there or isn't the user's. A session that ended already comes back as it is.
    public async Task<Session?> AbortAsync(Guid id, CancellationToken cancellationToken)
    {
        var (_, session) = await ChangeAsync(id, session => session.Abort(Now()), cancellationToken);
        return session;
    }

    public Task<(SessionAdvance? Result, Session? Session)> AdvanceAsync(Guid id, int step, CancellationToken cancellationToken) =>
        ChangeAsync<SessionAdvance?>(id, session => session.Advance(step, Now()), cancellationToken);

    // Null for a session that isn't there or isn't the user's, false for one that ended.
    public async Task<bool?> RecordActivityAsync(Guid id, CancellationToken cancellationToken)
    {
        var (recorded, session) = await ChangeAsync(id, session => session.RecordActivity(Now()), cancellationToken);
        return session is null ? null : recorded;
    }

    private async Task<(T? Result, Session? Session)> ChangeAsync<T>(Guid id, Func<Session, T> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (await sessions.FindForChangeAsync(id, cancellationToken) is not { } session)
            {
                return (default, null);
            }

            var result = change(session);
            if (await sessions.TrySaveAsync(cancellationToken))
            {
                return (result, session);
            }

            if (attempt == Attempts)
            {
                throw new InvalidOperationException($"Session {id} kept changing under {Attempts} attempts to store a signal.");
            }
        }
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}