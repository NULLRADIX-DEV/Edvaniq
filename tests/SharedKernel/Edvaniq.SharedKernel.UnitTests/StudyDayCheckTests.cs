using Edvaniq.ServiceDefaults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edvaniq.SharedKernel.UnitTests;

// Every process with the service defaults checks the time zone at startup.
[Collection(nameof(ExitCode))]
public class StudyDayCheckTests
{
    [Fact]
    public async Task ProcessWithTheZone_StartsNormally()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        Assert.Equal(0, Environment.ExitCode);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MissingZone_StopsWithOneLineAndExitCode1()
    {
        var lifetime = new Lifetime();
        var logger = new Lines();
        var check = new StudyDayCheck(lifetime, logger, () => throw new TimeZoneNotFoundException("no Europe/Berlin"));

        try
        {
            await check.StartAsync(TestContext.Current.CancellationToken);

            Assert.True(lifetime.Stopped);
            Assert.Equal(1, Environment.ExitCode);
            var line = Assert.Single(logger.Written);
            Assert.Equal("Critical: Time zone Europe/Berlin is not usable, stopping: no Europe/Berlin", line);
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public bool Stopped { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => Stopped = true;
    }

    private sealed class Lines : ILogger<StudyDayCheck>
    {
        public List<string> Written { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Written.Add($"{logLevel}: {formatter(state, exception)}" + (exception is null ? "" : " with exception"));
    }
}

[CollectionDefinition(nameof(ExitCode), DisableParallelization = true)]
public sealed class ExitCode;