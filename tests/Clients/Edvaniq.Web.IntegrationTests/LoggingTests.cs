using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Edvaniq.Web.IntegrationTests;

// On the server the console is the log. Every line carries the scopes, among them the TraceId of its request, so one
// id from a problem finds the lines of web host, gateway and services. ServiceDefaults sets this for every process,
// the web host stands in for them here.
public sealed class LoggingTests
{
    [Fact]
    public void OutsideDevelopment_EveryLineCarriesTheTraceId()
    {
        using var web = new WebFactory("http://127.0.0.1:9", configure: builder => builder.UseEnvironment("Production"));

        var console = web.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().CurrentValue;
        var factory = web.Services.GetRequiredService<IOptions<LoggerFactoryOptions>>().Value;

        Assert.True(console.IncludeScopes);
        Assert.True(console.SingleLine);
        Assert.True(factory.ActivityTrackingOptions.HasFlag(ActivityTrackingOptions.TraceId));
    }
}