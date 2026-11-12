using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.Api;

// dotnet Edvaniq.Services.Content.Api.dll ai pause|resume|status, run by the operator on the server. The emergency stop
// for every AI call of Content: paused, the worker asks the AI nothing and fails each document that would need it with
// "KI derzeit pausiert", a retry works once the AI runs again. Takes effect without a deploy and outlasts restarts.
internal static partial class AiCommand
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Invalid = 2;

    public static bool Matches(string[] args) => args is ["ai", ..];

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, TextWriter error)
    {
        if (args is not ["ai", "pause" or "resume" or "status"])
        {
            await error.WriteLineAsync("Usage: ai pause|resume|status");
            return Invalid;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AiCommand));
        try
        {
            using var scope = services.CreateScope();
            var aiSwitch = scope.ServiceProvider.GetRequiredService<IAiSwitch>();
            if (args[1] == "pause" && await aiSwitch.SetAsync(true, CancellationToken.None))
            {
                LogPaused(logger);
            }
            else if (args[1] == "resume" && await aiSwitch.SetAsync(false, CancellationToken.None))
            {
                LogResumed(logger);
            }

            await output.WriteLineAsync(await aiSwitch.IsPausedAsync(CancellationToken.None) ? "AI paused" : "AI running");
            return Ok;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            await error.WriteLineAsync("Switching the AI failed, see the log above.");
            return Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The operator paused the AI, documents that need it fail until it is resumed")]
    private static partial void LogPaused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The operator resumed the AI")]
    private static partial void LogResumed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Switching the AI failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}