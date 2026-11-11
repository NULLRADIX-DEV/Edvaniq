using Edvaniq.SharedKernel;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.ServiceDefaults;

// Every process needs the time zone data for the study day. Without it the process ends at startup with one log line
// and exit code 1, instead of failing later in a request or counting days in UTC.
internal sealed partial class StudyDayCheck(IHostApplicationLifetime lifetime, ILogger<StudyDayCheck> logger, Action check)
    : IHostedService
{
    public StudyDayCheck(IHostApplicationLifetime lifetime, ILogger<StudyDayCheck> logger)
        : this(lifetime, logger, StudyDay.EnsureAvailable)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            check();
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException
            or InvalidOperationException or TypeInitializationException)
        {
            LogMissingZone(logger, StudyDay.TimeZoneId, (exception.InnerException ?? exception).Message);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Critical, Message = "Time zone {TimeZone} is not usable, stopping: {Reason}")]
    private static partial void LogMissingZone(ILogger logger, string timeZone, string reason);
}