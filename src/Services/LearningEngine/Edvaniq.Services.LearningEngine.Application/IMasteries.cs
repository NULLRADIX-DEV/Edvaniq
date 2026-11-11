namespace Edvaniq.Services.LearningEngine.Application;

// The mastery of the current user's topics. The owner filter keeps other users' topics and records out.
public interface IMasteries
{
    // Null if the user doesn't know the topic (yet): someone else's and an unknown one look the same.
    Task<int?> FindAsync(Guid topicId, CancellationToken cancellationToken);
}