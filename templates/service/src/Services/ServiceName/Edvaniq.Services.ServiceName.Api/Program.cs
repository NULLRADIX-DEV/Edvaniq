using Edvaniq.Services.ServiceName.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
