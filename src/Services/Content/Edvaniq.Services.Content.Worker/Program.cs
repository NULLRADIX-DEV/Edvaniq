using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddWorkerInfrastructure();
builder.AddContentAi();

var host = builder.Build();
host.Run();
