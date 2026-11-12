using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// Once at the worker's start: without a monthly budget the AI has no limit, the log says so (AiBudgetMissing). A database
// that isn't there yet only costs the line, never the start.
public sealed class AiBudgetCheck(IServiceScopeFactory scopes, ILogger<AiBudgetCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            if ((await scope.ServiceProvider.GetRequiredService<IAiBudget>().StateAsync(stoppingToken)).Budget is null)
            {
                ContentLog.AiBudgetMissing(logger);
            }
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            ContentLog.AiBudgetUnknown(logger, exception.GetType().Name);
        }
    }
}