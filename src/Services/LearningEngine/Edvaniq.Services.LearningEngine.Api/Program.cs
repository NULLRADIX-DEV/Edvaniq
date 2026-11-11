using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Web;
using Edvaniq.Services.LearningEngine.Application;
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
        ? Results.Ok(new MasteryResponse(topicId, mastery))
        : Results.NotFound());

app.Run();

return 0;

// Whole percent, 0 to 100.
internal sealed record MasteryResponse(Guid TopicId, int Mastery);