using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Application;

// The documents of the current user. Infrastructure implements it, the owner filter keeps other users' documents out.
public interface IDocuments
{
    // Text and title must have passed Document.CheckText and Document.CheckTitle.
    Task<Document> PasteAsync(LearningGoalInfo learningGoal, string? title, string text, CancellationToken cancellationToken);

    // The file must be stored and its content must have passed FileCheck.
    Task<Document> UploadAsync(LearningGoalInfo learningGoal, string? fileName, Guid fileId, FileFormat format, CancellationToken cancellationToken);

    // How many documents of the user the learning goal holds, whatever their status.
    Task<int> CountInGoalAsync(Guid learningGoalId, CancellationToken cancellationToken);

    // Newest first.
    Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken cancellationToken);

    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken);

    // Starts a failed analysis again (Document.Retry). The document is null when the user has none with this id.
    Task<(RetryResult Result, Document? Document)> RetryAsync(Guid id, CancellationToken cancellationToken);

    // Puts a ready document back to waiting for its questions (Document.ReopenForQuestions), for the operator (#1018).
    Task<ReopenResult> ReopenAsync(Guid id, CancellationToken cancellationToken);

    // Deletes the document with its sections and reports DocumentDeleted in the same save. False when the user has none
    // with this id.
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public enum RetryResult
{
    Started,

    NotFound,

    // Not failed, or someone else started it again first.
    NotFailed,
}

public enum ReopenResult
{
    Reopened,

    NotFound,

    // Still in analysis, failed, or changed by someone else meanwhile.
    NotReady,
}

// A document without its text.
public sealed record DocumentSummary(
    Guid Id, string Title, DocumentStatus Status, string? FailureReason, FailureCode? FailureCode, DateTime CreatedAt, bool IsFromFile);