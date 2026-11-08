using Edvaniq.Services.Identity.Api.Accounts;
using Edvaniq.Services.Identity.Api.Tokens;
using Edvaniq.Services.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenIssuer();
builder.AddInfrastructure();

var app = builder.Build();

// Migrating is its own step (dotnet <service>.dll migrate). A normal start never changes the database.
if (args is ["migrate"])
{
    return app.RunMigrations();
}

// Sets up a team account, run by the operator on the server. Never starts the web server.
if (AccountCommand.Matches(args))
{
    // Disposed at the end like the migrate step, so the last log lines get out.
    await using (app)
    {
        return await AccountCommand.RunAsync(app.Services, args, Console.In, Console.Out, Console.Error);
    }
}

app.MapDefaultEndpoints();
app.MapTokenEndpoints();

app.Run();

return 0;