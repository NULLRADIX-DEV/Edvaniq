using Edvaniq.Services.ServiceName.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();

var app = builder.Build();

// Migrating is its own step (dotnet <service>.dll migrate). A normal start never changes the database.
if (args is ["migrate"])
{
    return app.RunMigrations();
}

app.MapDefaultEndpoints();

app.Run();

return 0;
