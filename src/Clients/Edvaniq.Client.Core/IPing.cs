namespace Edvaniq.Client.Core;

// The ping of the walking skeleton: from the client through web host, gateway and Planning into its database and back.
// An answer proves the whole way works.
public interface IPing
{
    // How the ping ended: with the value the database stored, or without it when it did not get through.
    Task<PingOutcome> SendAsync(CancellationToken cancellationToken = default);
}

public sealed record PingResult(int Id, DateTimeOffset PingedAt);

// A ping that did not get through names the trace of the failed request when the host answered with a problem, so the
// user can pass the id on.
public sealed record PingOutcome(PingResult? Result, string? TraceId = null)
{
    public static PingOutcome Failed(string? traceId = null) => new(null, traceId);
}