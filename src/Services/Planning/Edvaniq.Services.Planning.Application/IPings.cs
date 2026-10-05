using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Application;

// The ping of the walking skeleton. Infrastructure implements it against the service's own database.
public interface IPings
{
    // Stores the ping and returns it as read back from the database.
    Task<PingRecord> RecordAsync(CancellationToken cancellationToken);
}
