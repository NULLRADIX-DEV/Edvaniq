namespace Edvaniq.BuildingBlocks.Messaging;

// The stream a consumer listens to does not exist yet: its sender has not started or sends nothing so far. The consumer
// host waits for it without a warning.
public sealed class StreamMissingException(string stream, Exception? innerException = null)
    : Exception($"The stream {stream} does not exist yet.", innerException)
{
    public string Stream { get; } = stream;
}