using System.Text.Json;

namespace Edvaniq.BuildingBlocks.Messaging;

// How events are written into the outbox and read by the consumers. One setting for both sides.
internal static class MessageJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}