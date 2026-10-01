using Edvaniq.Services.ServiceName.Domain;

namespace Edvaniq.Services.ServiceName.Application;

// The example items of the current user. Infrastructure implements it, the owner filter keeps other users' items out.
public interface IExampleItems
{
    Task<ExampleItem> AddAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExampleItem>> ListAsync(CancellationToken cancellationToken);

    Task<ExampleItem?> FindAsync(Guid id, CancellationToken cancellationToken);
}
