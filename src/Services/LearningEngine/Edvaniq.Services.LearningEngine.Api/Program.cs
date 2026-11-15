using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Web;
using Edvaniq.Services.LearningEngine.Application;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenValidation();
builder.AddInfrastructure();

var app = builder.Build();

// Migrating is its own step (dotnet <service>.dll migrate). A normal start never changes the database.
if (args is ["migrate"])
{
    return app.RunMigrations();
}

// Errors become problems that name the trace. First, so they wrap health and the token check too.
app.UseProblems();

// Health first, then the token check: health must answer without a token. Called by hand, the two run after it,
// otherwise ASP.NET Core puts them in front of the whole pipeline.
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

// Who the token says the caller is. Smoke test for the token check, ignores any user id in the request.
app.MapGet("/me", (ICurrentUser user) => new { user.Id });

// Someone else's topic and one this service doesn't know (yet) look the same: 404. A hidden topic still answers: it is
// asked for by its id, practising it or showing an answer given just before it was hidden must not end in a 404 (#390).
app.MapGet("/mastery/{topicId:guid}", async (Guid topicId, IMasteries masteries, CancellationToken cancellationToken) =>
    await masteries.FindAsync(topicId, cancellationToken) is { } standing
        ? Results.Ok(new MasteryResponse(topicId, standing.Mastery, StateNames.Of(standing.State), Mastery.RuleVersion, standing.LastAnswerId))
        : Results.NotFound());

// All of the user's topics, or those of one learning goal. A goal that isn't the user's gives an empty list, like one
// without topics: this service doesn't own learning goals. Parsed by hand, so a broken id is a 400 with its field.
app.MapGet("/mastery", async (string? learningGoalId, IMasteries masteries, CancellationToken cancellationToken) =>
{
    Guid? goal = null;
    if (learningGoalId is not null)
    {
        if (!Guid.TryParse(learningGoalId, out var parsed))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["learningGoalId"] = ["Must be a GUID."] });
        }

        goal = parsed;
    }

    var topics = await masteries.ListAsync(goal, cancellationToken);
    return Results.Ok(new MasteryListResponse(
        Mastery.RuleVersion,
        [.. topics.Select(topic => new MasteryListEntry(topic.TopicId, topic.Mastery, StateNames.Of(topic.State), topic.LastAnswerId))]));
});

// Topics of one learning goal to refresh: secure at their last answer, below secure now only by the decay since. The
// goal is required, like /mastery a goal that isn't the user's gives an empty list.
app.MapGet("/mastery/decayed", async (string? learningGoalId, IMasteries masteries, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(learningGoalId, out var goal))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["learningGoalId"] = [learningGoalId is null ? "Is required." : "Must be a GUID."],
        });
    }

    var topics = await masteries.DecayedAsync(goal, cancellationToken);
    return Results.Ok(new DecayedListResponse(
        Mastery.RuleVersion,
        TopicStates.SecureFrom,
        [.. topics.Select(topic => new DecayedListEntry(
            topic.TopicId, topic.Standing.Mastery, topic.Standing.MasteryAtLastAnswer, topic.Standing.LastAnsweredAt))]));
});

// The user's weaknesses, the worst first, and secure topics (#453), of all topics or those of one learning goal. A goal
// that isn't the user's gives two empty lists, like /mastery.
app.MapGet("/mastery/weak-and-secure", async (string? learningGoalId, IMasteries masteries, CancellationToken cancellationToken) =>
{
    Guid? goal = null;
    if (learningGoalId is not null)
    {
        if (!Guid.TryParse(learningGoalId, out var parsed))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["learningGoalId"] = ["Must be a GUID."] });
        }

        goal = parsed;
    }

    var topics = await masteries.WeakAndSecureAsync(goal, cancellationToken);
    return Results.Ok(new WeakAndSecureResponse(
        Mastery.RuleVersion,
        [.. topics.Weaknesses.Select(topic => new WeaknessEntry(
            topic.TopicId, topic.Mastery, topic.Reason == WeaknessReason.RecentErrors ? "recentErrors" : "mastery"))],
        [.. topics.Secure.Select(topic => new SecureEntry(topic.TopicId, topic.Mastery))]));
});

// How topics stood at the start and the end of a session and whether its answers have arrived (#403), for Planning's
// session result. Ids repeat in the query: topicId, answerId. From and to are UTC. Topics the user doesn't know are left
// out. The limits keep the address short: a block has a quiz of at most 20 questions and a few check questions per
// session, Planning asks for one block. Answers still missing well after the end are worth a warning (#417).
app.MapGet("/mastery/changes", async (
    HttpRequest request, IMasteries masteries, ILoggerFactory loggers, TimeProvider time, CancellationToken cancellationToken) =>
{
    var errors = new Dictionary<string, string[]>();
    var topicIds = Guids(request, "topicId", MasteryChangeLimits.Topics, errors);
    var answerIds = Guids(request, "answerId", MasteryChangeLimits.Answers, errors);
    var from = Moment(request, "from", errors);
    var to = Moment(request, "to", errors);
    if (topicIds is { Count: 0 })
    {
        errors["topicId"] = ["Is required."];
    }

    if (from > to)
    {
        errors["to"] = ["Must not be before from."];
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var changes = await masteries.ChangesAsync(topicIds!, from!.Value, to!.Value, answerIds!, cancellationToken);
    LearningEngineLog.IfAnswersLate(
        loggers.CreateLogger("Edvaniq.Services.LearningEngine.MasteryChanges"), changes.MissingAnswers, answerIds!.Count, to.Value, time.GetUtcNow().UtcDateTime);
    return Results.Ok(new MasteryChangesResponse(
        Mastery.RuleVersion,
        changes.MissingAnswers,
        [.. changes.Topics.Select(topic => new TopicChangeEntry(
            topic.TopicId, StandingEntry.Of(topic.Before), StandingEntry.Of(topic.After), topic.Introduced))]));
});

app.Run();

return 0;

static List<Guid>? Guids(HttpRequest request, string name, int limit, Dictionary<string, string[]> errors)
{
    var values = request.Query[name];
    if (values.Count > limit)
    {
        errors[name] = [$"At most {limit}."];
        return null;
    }

    var ids = new List<Guid>();
    foreach (var value in values)
    {
        if (!Guid.TryParse(value, out var id))
        {
            errors[name] = ["Must be a GUID."];
            return null;
        }

        ids.Add(id);
    }

    return [.. ids.Distinct()];
}

static DateTime? Moment(HttpRequest request, string name, Dictionary<string, string[]> errors)
{
    if (DateTime.TryParse(
        request.Query[name].ToString(),
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
        out var moment))
    {
        return moment;
    }

    errors[name] = [request.Query.ContainsKey(name) ? "Must be a date and time." : "Is required."];
    return null;
}

// Whole percent, 0 to 100, the topic state and the version of the rule that worked both out. LastAnswerId names the
// newest answer LearningEngine has stored for the topic, so a client knows when its answer has arrived (#222).
internal sealed record MasteryResponse(Guid TopicId, int Mastery, string State, int RuleVersion, Guid? LastAnswerId);

// An object, not a bare list, so fields can be added later.
internal sealed record MasteryListResponse(int RuleVersion, IReadOnlyList<MasteryListEntry> Topics);

internal sealed record MasteryListEntry(Guid TopicId, int Mastery, string State, Guid? LastAnswerId);

// Threshold: the mastery a topic has fallen below, TopicStates.SecureFrom. LastAnsweredAt in UTC.
internal sealed record DecayedListResponse(int RuleVersion, int Threshold, IReadOnlyList<DecayedListEntry> Topics);

internal sealed record DecayedListEntry(Guid TopicId, int Mastery, int MasteryAtLastAnswer, DateTime LastAnsweredAt);

// Weaknesses the worst first, secure topics the highest first. Reason: "mastery" when the mastery is below 50, else
// "recentErrors", the last answers were mostly wrong.
internal sealed record WeakAndSecureResponse(int RuleVersion, IReadOnlyList<WeaknessEntry> Weaknesses, IReadOnlyList<SecureEntry> Secure);

internal sealed record WeaknessEntry(Guid TopicId, int Mastery, string Reason);

internal sealed record SecureEntry(Guid TopicId, int Mastery);

// MissingAnswers: how many of the answers named have no learning record yet. Before is null when the start lies before
// what the kept records still tell. Introduced: the topic has its introduction (from SessionCompleted).
internal sealed record MasteryChangesResponse(int RuleVersion, int MissingAnswers, IReadOnlyList<TopicChangeEntry> Topics);

internal sealed record TopicChangeEntry(Guid TopicId, StandingEntry? Before, StandingEntry? After, bool Introduced);

internal sealed record StandingEntry(int Mastery, string State)
{
    public static StandingEntry? Of(TopicStanding? standing) =>
        standing is null ? null : new(standing.Mastery, StateNames.Of(standing.State));
}

internal static class MasteryChangeLimits
{
    public const int Topics = 20;

    // A quiz of at most 20 questions and a few check questions in each session of the block. 100 ids keep the address
    // below 5 KB.
    public const int Answers = 100;
}

// The names on the wire, fixed here so renaming the enum can't change the contract.
internal static class StateNames
{
    public static string Of(TopicState state) => state switch
    {
        TopicState.New => "new",
        TopicState.Weakness => "weakness",
        TopicState.Building => "building",
        TopicState.Secure => "secure",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A topic state without a name on the wire."),
    };
}