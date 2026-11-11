using System.Text.Json;

namespace Edvaniq.BuildingBlocks.Contracts;

// How events look on the broker: the outbox writes and the consumers read with these options. A change here is a
// change of every event contract (docs/spec/events.md, "Der Vertrag im Code").
public static class EventJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}