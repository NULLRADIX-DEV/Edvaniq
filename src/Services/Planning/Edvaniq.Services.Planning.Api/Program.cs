using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Web;
using Edvaniq.Services.Planning.Api;
using Edvaniq.Services.Planning.Infrastructure;

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

// Health first, then the token check: health must answer without a token. Called by hand, the two run after it,
// otherwise ASP.NET Core puts them in front of the whole pipeline.
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

// Who the token says the caller is. Smoke test for the token check, ignores any user id in the request.
app.MapGet("/me", (ICurrentUser user) => new { user.Id });
app.MapExampleEndpoints();

app.Run();

return 0;
