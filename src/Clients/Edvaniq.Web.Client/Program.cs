using Edvaniq.Client.Core;

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// The browser only talks to the web host it was loaded from.
builder.Services.AddSingleton<IBackendStatus>(new HttpBackendStatus(new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(5),
}));

await builder.Build().RunAsync();