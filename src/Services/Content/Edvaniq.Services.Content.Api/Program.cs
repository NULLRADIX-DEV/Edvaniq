using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Web;
using Edvaniq.Services.Content.Api;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenValidation();
builder.AddInfrastructure();

// Planning gives every pasted text the user's default learning goal, asked with the caller's own token. The user waits
// at the button meanwhile: three tries of 2 s at most, and after 8 s the answer is 503 whatever happens.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ForwardUserToken>();

// Swaps the default resilience for shorter limits. Removing it is still marked experimental, the test with a hanging
// Planning (PlanningDown_AnswersWithin10Seconds) notices if it stops working.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<IDefaultLearningGoal, PlanningLearningGoals>(client => client.BaseAddress = PlanningLearningGoals.Address)
    .AddHttpMessageHandler<ForwardUserToken>()
    .RemoveAllResilienceHandlers()
#pragma warning restore EXTEXP0001
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = PlanningLearningGoals.TotalTimeout;
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(250);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
    });

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
app.MapDocumentEndpoints();

app.Run();

return 0;