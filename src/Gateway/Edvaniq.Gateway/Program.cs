using Edvaniq.BuildingBlocks.Web;
using Edvaniq.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The same token contract as every service (Authentication:Schemes:Bearer). Every route needs a valid token unless it
// says otherwise in its configuration, like the token endpoint of Identity. The services still check it themselves,
// the gateway passes it on unchanged.
builder.AddTokenValidation();
builder.Services.AddTokenProblems();

// Routes and targets come only from the section ReverseProxy. Service discovery turns a name like planning-api into
// the address: from Aspire locally, from services__<name>__http__0 in deploy/compose.yml on the server.
// Each cluster sets HttpRequest:ActivityTimeout below the 10 seconds a caller with the standard resilience handler
// waits per attempt. With the default of 100 seconds the caller would give up before the 504 problem arrives.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddServiceErrorProblems();

var app = builder.Build();

// An exception of the gateway itself becomes a bare 500 problem, without message or stack trace.
app.UseExceptionHandler();

app.MapDefaultEndpoints();

// After the health endpoints, which are middleware outside Development and would need a token otherwise.
app.UseAuthentication();
app.UseAuthorization();

// A custom pipeline replaces the default one, so the three steps of the default come back in by hand. Without them
// LoadBalancingPolicy, SessionAffinity and passive health checks in the configuration would be ignored.
app.MapReverseProxy(proxy =>
{
    proxy.UseForwarderProblems();
    proxy.UseSessionAffinity();
    proxy.UseLoadBalancing();
    proxy.UsePassiveHealthChecks();
});

// Whether the gateway itself answers. The web host forwards the status check of the shell here.
app.MapGet("/status", () => TypedResults.NoContent()).AllowAnonymous();

// Everything no route knows. "{**path}" instead of the default pattern, which leaves out paths that look like a file.
app.MapFallback("{**path}", () => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    detail: "No service answers to this path."))
    .AllowAnonymous();

app.Run();