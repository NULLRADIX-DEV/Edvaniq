using Edvaniq.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

// Routes and targets come only from the section ReverseProxy. Service discovery turns a name like planning-api into
// the address: from Aspire locally, from services__<name>__http__0 in deploy/compose.yml on the server.
// Each cluster sets HttpRequest:ActivityTimeout below the 10 seconds a caller with the standard resilience handler
// waits per attempt. With the default of 100 seconds the caller would give up before the 504 problem arrives.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();

// A custom pipeline replaces the default one, so the three steps of the default come back in by hand. Without them
// LoadBalancingPolicy, SessionAffinity and passive health checks in the configuration would be ignored.
app.MapReverseProxy(proxy =>
{
    proxy.UseForwarderProblems();
    proxy.UseSessionAffinity();
    proxy.UseLoadBalancing();
    proxy.UsePassiveHealthChecks();
});

// Everything no route knows. "{**path}" instead of the default pattern, which leaves out paths that look like a file.
app.MapFallback("{**path}", () => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    detail: "No service answers to this path."));

app.Run();