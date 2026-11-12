using System.Globalization;

using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.Api;

// dotnet Edvaniq.Services.Content.Api.dll ai pause|resume|status|budget <amount>|budget none, run by the operator on the
// server. The emergency stop and the monthly budget for every AI call of Content: paused or with this month's budget
// used up, the worker asks the AI nothing and fails each document that would need it with a reason, a retry works once
// the AI may run again. Takes effect without a deploy and outlasts restarts. Amounts only in this output, for the
// operator, never in a log line.
internal static partial class AiCommand
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Invalid = 2;

    public const string Usage = "Usage: ai pause|resume|status|budget <amount>|budget none, the amount per UTC month in the provider's billing currency, like 25.50";

    public static bool Matches(string[] args) => args is ["ai", ..];

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, TextWriter error)
    {
        decimal? budget = null;
        var valid = args switch
        {
            ["ai", "pause" or "resume" or "status"] => true,
            ["ai", "budget", "none"] => true,
            ["ai", "budget", var amount] => decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0 && (budget = parsed) is not null,
            _ => false,
        };
        if (!valid)
        {
            await error.WriteLineAsync(Usage);
            return Invalid;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AiCommand));
        try
        {
            using var scope = services.CreateScope();
            var aiSwitch = scope.ServiceProvider.GetRequiredService<IAiSwitch>();
            var aiBudget = scope.ServiceProvider.GetRequiredService<IAiBudget>();
            switch (args[1])
            {
                case "pause" when await aiSwitch.SetAsync(true, CancellationToken.None):
                    LogPaused(logger);
                    break;
                case "resume" when await aiSwitch.SetAsync(false, CancellationToken.None):
                    LogResumed(logger);
                    break;
                case "budget" when await aiBudget.SetAsync(budget, CancellationToken.None):
                    LogBudgetChanged(logger, budget is not null);
                    break;
            }

            var state = await aiBudget.StateAsync(CancellationToken.None);
            var spent = state.Spent.ToString("0.00####", CultureInfo.InvariantCulture);
            await output.WriteLineAsync(
                (await aiSwitch.IsPausedAsync(CancellationToken.None) ? "AI paused" : "AI running")
                + (state.Budget is { } limit
                    ? $", spent {spent} of {limit.ToString("0.00####", CultureInfo.InvariantCulture)} this month ({state.Percent} %)"
                    : $", spent {spent} this month, no monthly budget"));
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

    // Whether there is a budget now, never its amount.
    [LoggerMessage(Level = LogLevel.Information, Message = "The operator changed the monthly AI budget, a budget is set: {HasBudget}")]
    private static partial void LogBudgetChanged(ILogger logger, bool hasBudget);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Switching the AI failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}