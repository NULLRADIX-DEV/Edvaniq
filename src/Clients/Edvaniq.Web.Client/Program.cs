using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var host = new Uri(builder.HostEnvironment.BaseAddress);

// The browser only talks to the web host it was loaded from, and keeps the session cookie for all calls itself.
builder.Services.AddSingleton<IBackendStatus>(new HttpBackendStatus(new HttpClient
{
    BaseAddress = host,
    Timeout = TimeSpan.FromSeconds(5),
}));

// What a run-out login touches: the watch that notices it and the drafts that keep what was not sent. Scoped is one per
// app in the browser, and these live only here and in the MAUI app, never in the web host.
builder.Services.AddScoped<SessionWatch>();
builder.Services.AddScoped<Drafts>();

// The host answers within 9 seconds, its wait for a token or for Identity included, with an error of its own if need be.
// Every call under /api goes past the watch.
HttpClient Watched(IServiceProvider services) =>
    new(new SessionWatchHandler(services.GetRequiredService<SessionWatch>()) { InnerHandler = new HttpClientHandler() })
    {
        BaseAddress = host,
        Timeout = TimeSpan.FromSeconds(20),
    };

builder.Services.AddScoped<IPing>(services => new HttpPing(Watched(services)));
builder.Services.AddScoped<ILearningMaterial>(services => new HttpLearningMaterial(Watched(services)));
builder.Services.AddScoped<IQuiz>(services => new HttpQuiz(Watched(services)));
builder.Services.AddSingleton<IWebSession>(new HttpWebSession(new HttpClient
{
    BaseAddress = host,
    Timeout = TimeSpan.FromSeconds(20),
}));

builder.Services.AddSingleton<IReturnSignal, BrowserReturnSignal>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationState>();

await builder.Build().RunAsync();