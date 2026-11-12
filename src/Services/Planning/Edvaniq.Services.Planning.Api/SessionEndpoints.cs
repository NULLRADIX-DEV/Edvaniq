using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// Sessions of the user from the token (#376). Broken off and moved on with #393, active learning time with #394,
// finished with #395.
internal static class SessionEndpoints
{
    public const string BlockGone = "Diesen Block gibt es im aktuellen Plan nicht mehr. Lade den Plan neu.";

    public const string BlockDone = "Dieser Block ist schon erledigt.";

    public const string SessionEnded = "Diese Session ist schon beendet.";

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

        // Breaking off ends the session, the block stays open and the next start goes on from here. Doing it again
        // gives the session as it is.
        group.MapPost("/{id:guid}/abort", async (Guid id, SessionChanges changes, CancellationToken cancellationToken) =>
            await changes.AbortAsync(id, cancellationToken) is { } session
                ? Results.Ok(SessionResponse.From(session, existing: true))
                : Results.NotFound());

        // Only forward: a step at or before the current one changes nothing.
        group.MapPost("/{id:guid}/advance", async (Guid id, AdvanceRequest request, SessionChanges changes, CancellationToken cancellationToken) =>
        {
            if (request.Step is not { } step)
            {
                return NoSuchStep();
            }

            var (result, session) = await changes.AdvanceAsync(id, step, cancellationToken);
            return result switch
            {
                null => Results.NotFound(),
                SessionAdvance.NoSuchStep => NoSuchStep(),
                SessionAdvance.Ended => Results.Problem(title: SessionEnded, statusCode: StatusCodes.Status409Conflict),
                _ => Results.Ok(SessionResponse.From(session!, existing: true)),
            };
        });

        // The user is working on the session (#394): the client says so while a step is open, the server takes the
        // moment from its own clock.
        group.MapPost("/{id:guid}/activity", async (Guid id, SessionChanges changes, CancellationToken cancellationToken) =>
            await changes.RecordActivityAsync(id, cancellationToken) switch
            {
                null => Results.NotFound(),
                false => Results.Problem(title: SessionEnded, statusCode: StatusCodes.Status409Conflict),
                true => Results.NoContent(),
            });

        return app;
    }

    private static IResult NoSuchStep() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(AdvanceRequest.Step)] = ["Diesen Schritt hat die Session nicht."] });
}

internal sealed record StartRequest(Guid BlockId);

// Step: the index in Steps to move to.
internal sealed record AdvanceRequest(int? Step);

// Kind is "learn" or "review". Existing: the user had this session already, a start brought it back. EndedAt is null
// while it runs. Steps: the fixed order of the session (#377), each "review", "introductionUnit" or "quiz", Empty when
// there is nothing to do in it (the review until M4), Questions the size the client passes to POST /quizzes of
// Assessment for the quiz. CurrentStep: the index in Steps the user is at. Outcome: "running" or "aborted" (#393).
// ActiveSeconds: the active learning time up to the last signal (#394).
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
    bool Existing,
    IReadOnlyList<StepResponse> Steps,
    int CurrentStep,
    string Outcome,
    int ActiveSeconds)
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
            existing,
            [.. session.Steps.Select(step => new StepResponse(NameOf(step.Kind), step.Empty, step.Questions))],
            session.CurrentStep,
            session.Outcome == SessionOutcome.Aborted ? "aborted" : "running",
            session.ActiveSeconds);

    private static string NameOf(SessionStepKind kind) => kind switch
    {
        SessionStepKind.Review => "review",
        SessionStepKind.IntroductionUnit => "introductionUnit",
        _ => "quiz",
    };
}

internal sealed record StepResponse(string Kind, bool Empty, int? Questions);