using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// Sessions of the user from the token (#376). Ending one comes with #393/#395.
internal static class SessionEndpoints
{
    public const string BlockGone = "Diesen Block gibt es im aktuellen Plan nicht mehr. Lade den Plan neu.";

    public const string BlockDone = "Dieser Block ist schon erledigt.";

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sessions");

        // 201 for a new session. 200 with Existing for the one the user already has, the client says so (A88).
        group.MapPost("/", async (StartRequest request, SessionStarts starts, CancellationToken cancellationToken) =>
        {
            var start = await starts.StartAsync(request.BlockId, cancellationToken);
            return start.Result switch
            {
                SessionStartResult.Started => Results.Created($"/sessions/{start.Session!.Id}", SessionResponse.From(start.Session, existing: false)),
                SessionStartResult.Existing => Results.Ok(SessionResponse.From(start.Session!, existing: true)),
                SessionStartResult.BlockDone => Results.Problem(title: BlockDone, statusCode: StatusCodes.Status409Conflict),
                _ => Results.Problem(title: BlockGone, statusCode: StatusCodes.Status404NotFound),
            };
        });

        group.MapGet("/active", async (ISessions sessions, CancellationToken cancellationToken) =>
            await sessions.ActiveAsync(cancellationToken) is { } active
                ? Results.Ok(SessionResponse.From(active, existing: true))
                : Results.NoContent());

        // Another user's session is a 404 like one that isn't there.
        group.MapGet("/{id:guid}", async (Guid id, ISessions sessions, CancellationToken cancellationToken) =>
            await sessions.FindAsync(id, cancellationToken) is { } session
                ? Results.Ok(SessionResponse.From(session, existing: true))
                : Results.NotFound());

        return app;
    }
}

internal sealed record StartRequest(Guid BlockId);

// Kind is "learn" or "review". Existing: the user had this session already, a start brought it back. EndedAt is null
// while it runs.
internal sealed record SessionResponse(
    Guid Id,
    Guid BlockId,
    Guid TopicId,
    string Kind,
    int Minutes,
    DateOnly StudyDay,
    Guid LearningGoalId,
    DateTime StartedAt,
    DateTime? EndedAt,
    bool Existing)
{
    public static SessionResponse From(Session session, bool existing) =>
        new(
            session.Id,
            session.BlockId,
            session.TopicId,
            session.Kind == BlockKind.Review ? "review" : "learn",
            session.Minutes,
            session.StudyDay,
            session.LearningGoalId,
            session.StartedAt,
            session.EndedAt,
            existing);
}