using Edvaniq.Services.Identity.Api.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddTokenIssuer();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapTokenEndpoints();

app.Run();
