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

// Someone else's topic and one this service doesn't know (yet) look the same: 404.
app.MapGet("/mastery/{topicId:guid}", async (Guid topicId, IMasteries masteries, CancellationToken cancellationToken) =>
    await masteries.FindAsync(topicId, cancellationToken) is { } standing
        ? Results.Ok(new MasteryResponse(topicId, standing.Mastery, StateNames.Of(standing.State), Mastery.RuleVersion))
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
        [.. topics.Select(topic => new MasteryListEntry(topic.TopicId, topic.Mastery, StateNames.Of(topic.State)))]));
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

app.Run();

return 0;

// Whole percent, 0 to 100, the topic state and the version of the rule that worked both out.
internal sealed record MasteryResponse(Guid TopicId, int Mastery, string State, int RuleVersion);

// An object, not a bare list, so fields can be added later.
internal sealed record MasteryListResponse(int RuleVersion, IReadOnlyList<MasteryListEntry> Topics);

internal sealed record MasteryListEntry(Guid TopicId, int Mastery, string State);

// Threshold: the mastery a topic has fallen below, TopicStates.SecureFrom. LastAnsweredAt in UTC.
internal sealed record DecayedListResponse(int RuleVersion, int Threshold, IReadOnlyList<DecayedListEntry> Topics);

internal sealed record DecayedListEntry(Guid TopicId, int Mastery, int MasteryAtLastAnswer, DateTime LastAnsweredAt);

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