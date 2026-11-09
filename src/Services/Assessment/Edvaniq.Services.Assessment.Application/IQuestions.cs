using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The questions of all users, for the handlers. They run without a current user.
public interface IQuestions
{
    // Which of these ids are stored already, whoever they belong to.
    Task<IReadOnlySet<Guid>> KnownAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(Question question);
}