namespace Edvaniq.Client.Core;

// The user's pasted texts (Content) and the topics their analysis brought (Knowledge), both through the web host.
public interface ILearningMaterial
{
    // The user's documents, newest first, without their text.
    Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    // A new document in status Uploaded. A text Content refuses comes back as Rejected with Content's messages.
    Task<CallOutcome<DocumentInfo>> PasteAsync(string? title, string text, CancellationToken cancellationToken = default);

    // All of the user's topics on both levels, oldest first.
    Task<CallOutcome<IReadOnlyList<TopicInfo>>> ListTopicsAsync(CancellationToken cancellationToken = default);
}

public enum DocumentStatus
{
    Uploaded,
    Analyzing,
    Ready,
    Failed,
}

public sealed record DocumentInfo(Guid Id, string Title, DocumentStatus Status, DateTimeOffset CreatedAt, string? FailureReason = null);

// A topic of the upper level with its subtopics. DocumentId names the document whose analysis brought it, null for a
// topic that came another way.
public sealed record TopicInfo(Guid Id, string Name, IReadOnlyList<SubtopicInfo> Subtopics, Guid? DocumentId = null)
{
    public bool CameFrom(Guid documentId) =>
        DocumentId == documentId || Subtopics.Any(subtopic => subtopic.DocumentId == documentId);
}

public sealed record SubtopicInfo(Guid Id, string Name, Guid? DocumentId = null);

public enum CallResult
{
    Ok,

    // Nobody is signed in any more, the login ran out or ended elsewhere.
    SignedOut,

    // The service refused what was sent and said why (Messages).
    Rejected,

    // Too big to get through at all.
    TooLarge,

    // There is no such thing for this user, or not any more.
    NotFound,

    // It's there, but what was asked for isn't ready yet, the questions of a new topic for instance.
    NotReady,

    // Did not get through: no connection, no answer in time, an error of the server.
    Failed,
}

// How a call ended. A failed call names the trace of the problem the host answered with, if there was one.
public sealed record CallOutcome<T>(CallResult Result, T? Value = default, string? TraceId = null, IReadOnlyList<string>? Messages = null)
{
    public static readonly CallOutcome<T> SignedOut = new(CallResult.SignedOut);

    public static CallOutcome<T> Ok(T value) => new(CallResult.Ok, value);

    public static CallOutcome<T> Failed(string? traceId = null) => new(CallResult.Failed, TraceId: traceId);
}