using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The operator's monthly budget for the AI (Domain.AiSwitch) and what the AI cost this UTC month. The Api asks before it
// takes a new job, the worker before every AI call, both with the same sum.
public interface IAiBudget
{
    Task<AiBudgetState> StateAsync(CancellationToken cancellationToken);

    // Null takes the limit away. Whether this changed anything.
    Task<bool> SetAsync(decimal? budget, CancellationToken cancellationToken);

    // Notes that the operator was told about this month's state: true only the first time in the month for each of the
    // warning and the exhausted budget, so each log line goes out once.
    Task<(bool Warn, bool Exhausted)> NoteAsync(AiBudgetState state, CancellationToken cancellationToken);
}