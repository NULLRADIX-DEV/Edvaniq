using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// The browser only talks to the web host it was loaded from, and keeps the session cookie for all calls itself.
builder.Services.AddSingleton<IBackendStatus>(new HttpBackendStatus(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(5),
}));

// The host answers within 9 seconds, its wait for a token or for Identity included, with an error of its own if need be.
builder.Services.AddSingleton<IPing>(new HttpPing(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(20),
}));
builder.Services.AddSingleton<ILearningMaterial>(new HttpLearningMaterial(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(20),
}));
builder.Services.AddSingleton<IQuiz>(new HttpQuiz(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(20),
}));
builder.Services.AddSingleton<IWebSession>(new HttpWebSession(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(20),
}));

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationState>();

await builder.Build().RunAsync();