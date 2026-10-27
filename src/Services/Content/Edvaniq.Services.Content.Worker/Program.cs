using Edvaniq.Services.Content.Infrastructure.Ai;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddContentAi();

var host = builder.Build();
host.Run();
