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
    await masteries.FindAsync(topicId, cancellationToken) is { } mastery
        ? Results.Ok(new MasteryResponse(topicId, mastery, Mastery.RuleVersion))
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
    return Results.Ok(new MasteryListResponse(Mastery.RuleVersion, topics));
});

app.Run();

return 0;

// Whole percent, 0 to 100, and the version of the rule that worked it out.
internal sealed record MasteryResponse(Guid TopicId, int Mastery, int RuleVersion);

// An object, not a bare list, so fields can be added later.
internal sealed record MasteryListResponse(int RuleVersion, IReadOnlyList<TopicMastery> Topics);