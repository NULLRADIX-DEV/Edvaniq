using Edvaniq.Services.Planning.Contracts;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The event of a session that just ended, the same whether the user ended it or its topic left the goal (#412).
public static class SessionEnds
{
    // unitStartedAt: Session.UnitStartedAt, the session's own start when it isn't known.
    public static SessionCompleted EventOf(Session session, DateTime? unitStartedAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new SessionCompleted(
            session.OwnerId,
            session.Id,
            session.EndedAt ?? throw new InvalidOperationException("A session that runs has not ended."),
            session.Outcome == SessionOutcome.Completed,
            session.IntroducedTopicIds(),
            unitStartedAt ?? session.StartedAt);
    }
}