namespace Edvaniq.Services.Assessment.Application;

// Asks Knowledge, as the current user, which analysis a topic came from. Until #214 that is the only way from a topic to
// its questions. Knowledge decides whose topic it is, a foreign one looks like none.
public interface ITopicSource
{
    // Null if the user has no such topic. Throws TopicSourceUnavailableException if Knowledge can't tell.
    Task<TopicSource?> FindAsync(Guid topicId, CancellationToken cancellationToken);
}

// DocumentId is null for a topic that came from no analysis, it has no questions.
public sealed record TopicSource(Guid? DocumentId, int Position);

public sealed class TopicSourceUnavailableException(Exception innerException)
    : Exception("Knowledge could not be asked for the topic.", innerException);