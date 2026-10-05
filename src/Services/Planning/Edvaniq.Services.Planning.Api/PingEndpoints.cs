using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// The ping of the walking skeleton: an answer here proves the way through the token check into the service's own
// database. POST, because every ping writes.
internal static class PingEndpoints
{
    public static IEndpointRouteBuilder MapPingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ping", async (IPings pings, CancellationToken cancellationToken) =>
            PingResponse.From(await pings.RecordAsync(cancellationToken)));

        return app;
    }
}

internal sealed record PingResponse(int Id, DateTime PingedAt)
{
    public static PingResponse From(PingRecord ping) => new(ping.Id, ping.PingedAt);
}
