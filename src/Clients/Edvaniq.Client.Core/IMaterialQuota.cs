namespace Edvaniq.Client.Core;

// What is left of the user's daily quotas in Content (#307), through the web host. Only for showing: Content checks on
// each upload itself and says why when it refuses.
public interface IMaterialQuota
{
    Task<CallOutcome<MaterialQuota>> GetAsync(CancellationToken cancellationToken = default);
}

// Generations are null when Content has no limit for them. ResetsAt in UTC.
public sealed record MaterialQuota(int PagesLeftToday, int PagesPerDay, int? GenerationsLeftToday, int? GenerationsPerDay, DateTime ResetsAt);