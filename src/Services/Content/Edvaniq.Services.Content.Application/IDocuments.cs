using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The documents of the current user. Infrastructure implements it, the owner filter keeps other users' documents out.
public interface IDocuments
{
    // Text and title must have passed Document.CheckText and Document.CheckTitle.
    Task<Document> PasteAsync(string? title, string text, CancellationToken cancellationToken);

    // Newest first.
    Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken);

    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken);
}

// A document without its text.
public sealed record DocumentSummary(Guid Id, string Title, DocumentStatus Status, string? FailureReason, DateTime CreatedAt);
