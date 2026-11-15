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

    public const string SessionRunning = "Diese Session läuft noch, ein Ergebnis gibt es erst nach dem Ende.";

    public const string AnswersUnavailable = "Die Antworten können gerade nicht geladen werden. Versuch es gleich noch einmal.";

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

        // What an ended session brought (#403), finished or broken off, counted for its block up to it. 409 while it
        // runs, 503 when Assessment, which keeps the answers, can't be asked.
        group.MapGet("/{id:guid}/result", async (Guid id, SessionResults results, CancellationToken cancellationToken) =>
        {
            var lookup = await results.OfAsync(id, cancellationToken);
            return lookup.Status switch
            {
                SessionResultStatus.NotFound => Results.NotFound(),
                SessionResultStatus.Running => Results.Problem(title: SessionRunning, statusCode: StatusCodes.Status409Conflict),
                SessionResultStatus.AnswersUnavailable => Results.Problem(title: AnswersUnavailable, statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Ok(SessionResultResponse.From(lookup)),
            };
        });

        // Breaking off ends the session, the block stays open and the next start goes on from here. Doing it again
        // gives the session as it is.
        group.MapPost("/{id:guid}/abort", async (Guid id, SessionChanges changes, CancellationToken cancellationToken) =>
            await changes.AbortAsync(id, cancellationToken) is { } session
                ? Results.Ok(SessionResponse.From(session, existing: true))
                : Results.NotFound());

        // Finishing ends the session and marks its block done (#395). Doing it again, or on a session broken off, gives the
        // session as it is and sends nothing.
        group.MapPost("/{id:guid}/complete", async (Guid id, SessionChanges changes, CancellationToken cancellationToken) =>
            await changes.CompleteAsync(id, cancellationToken) is { } session
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
// Assessment for the quiz. CurrentStep: the index in Steps the user is at. Outcome: "running", "aborted" (#393) or "completed" (#395).
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
            session.Outcome switch
            {
                SessionOutcome.Aborted => "aborted",
                SessionOutcome.Completed => "completed",
                _ => "running",
            },
            session.ActiveSeconds);

    private static string NameOf(SessionStepKind kind) => kind switch
    {
        SessionStepKind.Review => "review",
        SessionStepKind.IntroductionUnit => "introductionUnit",
        _ => "quiz",
    };
}

internal sealed record StepResponse(string Kind, bool Empty, int? Questions);

// Outcome as in SessionResponse. Answered, Correct, Wrong: the answers on the block up to this session, a shown solution
// counts as wrong. Complete: LearningEngine has every one of them and the introduction, only then Topics names the
// mastery before and after, the block's topic first; null otherwise, never an old value (#406). MasteryAvailable: false
// when LearningEngine couldn't be asked. DailyGoal as in /plan/today. NextStep: kind "block" with the block "Jetzt lernen"
// goes on with, or "bonusReview" when nothing is open.
internal sealed record SessionResultResponse(
    Guid SessionId,
    string Outcome,
    int Answered,
    int Correct,
    int Wrong,
    bool Complete,
    bool MasteryAvailable,
    IReadOnlyList<TopicChangeResponse>? Topics,
    string DailyGoal,
    NextStepResponse NextStep)
{
    public static SessionResultResponse From(SessionResultLookup lookup)
    {
        var session = lookup.Session!;
        var result = lookup.Result!;
        return new(
            session.Id,
            session.Outcome == SessionOutcome.Completed ? "completed" : "aborted",
            result.Answered,
            result.Correct,
            result.Wrong,
            result.Complete,
            lookup.MasteryAvailable,
            result.Topics?.Select(topic => new TopicChangeResponse(topic.TopicId, StandingResponse.Of(topic.Before), StandingResponse.Of(topic.After))).ToList(),
            lookup.DailyGoal switch
            {
                DailyGoalState.Reached => "reached",
                DailyGoalState.Open => "open",
                _ => "none",
            },
            lookup.NextBlock is { } next
                ? new NextStepResponse("block", next.Id, next.Block.TopicId, next.Block.Kind == BlockKind.Review ? "review" : "learn", next.Block.Minutes)
                : new NextStepResponse("bonusReview", null, null, null, null));
    }
}

// Before null when LearningEngine can't tell the start any more.
internal sealed record TopicChangeResponse(Guid TopicId, StandingResponse? Before, StandingResponse? After);

// State as LearningEngine names it, null for one Planning doesn't know.
internal sealed record StandingResponse(int Mastery, string? State)
{
    public static StandingResponse? Of(TopicStanding? standing) =>
        standing is null
            ? null
            : new(standing.Mastery, standing.State switch
            {
                TopicState.New => "new",
                TopicState.Weakness => "weakness",
                TopicState.Building => "building",
                TopicState.Secure => "secure",
                _ => null,
            });
}

// BlockKind "learn" or "review", the block's fields null for a bonus review.
internal sealed record NextStepResponse(string Kind, Guid? BlockId, Guid? TopicId, string? BlockKind, int? Minutes);