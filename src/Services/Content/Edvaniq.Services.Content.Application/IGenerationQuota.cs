using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The daily quota of generations a user may have (Domain.GenerationQuota). The Api asks before it takes a new job, the
// worker charges every topic once, in the save of its question pool.
public interface IGenerationQuota
{
    Task<GenerationQuotaState> StateOfAsync(string ownerId, CancellationToken cancellationToken);

    // Adds the charge to the open unit of work, the caller saves it with the pool.
    void Charge(Document document, Guid topicId);
}

// ResetsAt is the next 04:00 in Berlin, in UTC.
public sealed record GenerationQuotaState(int GenerationsPerDay, int Used, DateTime ResetsAt)
{
    public int Left => Math.Max(0, GenerationsPerDay - Used);

    // A document with more topics than a whole day allows would never fit. It may go on a day nothing was used yet and
    // takes the whole day then.
    public bool Allows(int generations) => generations <= Left || (generations > GenerationsPerDay && Used == 0);
}

// Content:GenerationsPerDay, how many generations one user may have per study day.
public sealed class GenerationQuotaOptions
{
    public int GenerationsPerDay { get; set; } = GenerationQuota.DefaultGenerationsPerDay;
}