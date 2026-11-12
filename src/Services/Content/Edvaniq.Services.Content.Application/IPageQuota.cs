using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The daily quota of pages a user may have processed (Domain.PageQuota). The Api asks before it takes a text or a file,
// the worker charges each document once before it splits it, both with the same count.
public interface IPageQuota
{
    // The user's state on the study day of now.
    Task<PageQuotaState> StateOfAsync(string ownerId, CancellationToken cancellationToken);

    // Whether the document has taken its pages already, on any day. A retry then goes on without asking the quota.
    Task<bool> IsChargedAsync(Guid documentId, CancellationToken cancellationToken);

    // Adds the charge to the open unit of work, the caller saves it with the document.
    void Charge(Document document, int pages);
}

// ResetsAt is the next 04:00 in Berlin, in UTC.
public sealed record PageQuotaState(int PagesPerDay, int Used, DateTime ResetsAt)
{
    public int Left => Math.Max(0, PagesPerDay - Used);

    // A document with more pages than a whole day allows, like a long PDF, would never fit. It may go on a day nothing
    // was used yet and takes the whole day then.
    public bool Allows(int pages) => pages <= Left || (pages > PagesPerDay && Used == 0);
}

// Content:PagesPerDay, how many pages one user may have processed per study day.
public sealed class PageQuotaOptions
{
    public const string Section = "Content";

    public int PagesPerDay { get; set; } = PageQuota.DefaultPagesPerDay;
}