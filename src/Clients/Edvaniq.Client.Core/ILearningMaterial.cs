namespace Edvaniq.Client.Core;

// The user's pasted texts (Content) and the topics their analysis brought (Knowledge), both through the web host.
public interface ILearningMaterial
{
    // The user's documents, newest first, without their text.
    Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    // A new document in status Uploaded. Both calls carry the current MaterialConfirmationValues.Version: only call them
    // once the user confirmed. A text Content refuses comes back as Rejected with Content's messages.
    Task<CallOutcome<DocumentInfo>> PasteAsync(string? title, string text, CancellationToken cancellationToken = default);

    // A new document in status Uploaded from a file, in the default learning goal. A file Content refuses comes back as
    // Rejected or TooLarge with Content's message.
    Task<CallOutcome<DocumentInfo>> UploadAsync(string fileName, Stream content, CancellationToken cancellationToken = default);

    // Starts the analysis of a failed document again, the same document goes on where it failed. One that isn't failed
    // (any more) comes back as Failed, the newest state tells what happened.
    Task<CallOutcome<DocumentInfo>> RetryAsync(Guid documentId, CancellationToken cancellationToken = default);

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

// Source says where it came from. One from a file stays Uploaded until the worker has read its text.
public sealed record DocumentInfo(
    Guid Id, string Title, DocumentStatus Status, DateTimeOffset CreatedAt, string? FailureReason = null, DocumentSource Source = DocumentSource.Text);

public enum DocumentSource
{
    Text,
    File,
}

// A topic of the upper level with its subtopics. DocumentId names the document whose analysis brought it first, null for
// a topic that came another way. Later documents only add sections to it.
// Hidden comes only when hidden ones are asked for (#286), a subtopic counts as hidden under a hidden topic.
public sealed record TopicInfo(Guid Id, string Name, IReadOnlyList<SubtopicInfo> Subtopics, Guid? DocumentId = null, bool Hidden = false);

public sealed record SubtopicInfo(Guid Id, string Name, Guid? DocumentId = null, bool Hidden = false);

public enum CallResult
{
    Ok,

    // Nobody is signed in any more, the login ran out or ended elsewhere.
    SignedOut,

    // The service refused what was sent and said why (Messages).
    Rejected,

    // Too big to get through at all.
    TooLarge,

    // A limit or the pause of the AI holds it up for now. Messages says why and when it goes on, if the service said.
    Limited,

    // There is no such thing for this user, or not any more.
    NotFound,

    // It's there, but what was asked for isn't ready yet, the questions of a new topic for instance.
    NotReady,

    // Did not get through: no connection, no answer in time, an error of the server.
    Failed,
}

// How a call ended. A failed call names the trace of the problem the host answered with, if there was one.
// A rejection of a form can name its message per field (FieldErrors, keyed by the field's name as the service has it).
public sealed record CallOutcome<T>(
    CallResult Result,
    T? Value = default,
    string? TraceId = null,
    IReadOnlyList<string>? Messages = null,
    IReadOnlyDictionary<string, string>? FieldErrors = null)
{
    public static readonly CallOutcome<T> SignedOut = new(CallResult.SignedOut);

    public static CallOutcome<T> Ok(T value) => new(CallResult.Ok, value);

    public static CallOutcome<T> Failed(string? traceId = null) => new(CallResult.Failed, TraceId: traceId);
}