namespace Edvaniq.Services.Content.Application;

// The operator's emergency stop for the AI (Domain.AiSwitch). Read before every AI call, so it takes effect at once.
public interface IAiSwitch
{
    Task<bool> IsPausedAsync(CancellationToken cancellationToken);

    // Whether this changed anything.
    Task<bool> SetAsync(bool paused, CancellationToken cancellationToken);
}