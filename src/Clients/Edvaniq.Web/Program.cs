using Edvaniq.Web;
using Edvaniq.Web.Components;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// On the server the host's nginx ends HTTPS and forwards to the web host through the gateway of the app's network.
// Only from the networks in ForwardedHeaders:KnownNetworks the web host takes scheme and client address from
// X-Forwarded-Proto and X-Forwarded-For. Without them it trusts only loopback, the default.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddWebSession();

// The browser never talks to the gateway itself. Everything under /api goes on from here, on the server side, with the
// routes from the section ReverseProxy. The gateway's address comes from service discovery, like in the gateway.
// The cluster waits 9 seconds, one more than the gateway waits for a service, so the gateway's own 504 problem gets
// through to the browser. Routes with the metadata BackendToken carry the web host's token to the backend.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddBackendToken();

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Pages get the not-found page. /api and /session opt out below (SkipStatusCodePages): a 401 or 502 there must stay
// what it is and not turn into an HTML page. It becomes a problem that names the trace instead.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseApiProblems();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Edvaniq.Client.UI.Routes).Assembly);

app.MapWebSessionEndpoints();
app.MapReverseProxy()
    .WithMetadata(new SkipStatusCodePagesAttribute());

app.MapDefaultEndpoints();

app.Run();