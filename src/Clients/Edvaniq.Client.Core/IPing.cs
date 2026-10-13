namespace Edvaniq.Client.Core;

// The ping of the walking skeleton: from the client through web host, gateway and Planning into its database and back.
// An answer proves the whole way works.
public interface IPing
{
    // The ping as the database stored it, or null when it did not get through.
    Task<PingResult?> SendAsync(CancellationToken cancellationToken = default);
}

public sealed record PingResult(int Id, DateTimeOffset PingedAt);