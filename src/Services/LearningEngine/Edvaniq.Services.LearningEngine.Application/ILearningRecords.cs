using Edvaniq.Services.LearningEngine.Domain;

namespace Edvaniq.Services.LearningEngine.Application;

// The learning records of all users, for the handlers. They run without a current user.
public interface ILearningRecords
{
    Task<bool> ExistsAsync(Guid answerId, CancellationToken cancellationToken);

    void Add(LearningRecord record);
}