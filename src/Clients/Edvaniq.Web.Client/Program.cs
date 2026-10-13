using Edvaniq.Client.Core;

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// The browser only talks to the web host it was loaded from.
builder.Services.AddSingleton<IBackendStatus>(new HttpBackendStatus(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(5),
}));

// The host answers within 9 seconds, its wait for a token included, with an error of its own if need be.
builder.Services.AddSingleton<IPing>(new HttpPing(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(20),
}));

await builder.Build().RunAsync();