namespace Edvaniq.BuildingBlocks.Messaging;

// The sender hasn't created its stream yet. The consumer host just waits, no warning.
public sealed class StreamMissingException(string stream, Exception? innerException = null)
    : Exception($"The stream {stream} does not exist yet.", innerException)
{
    public string Stream { get; } = stream;
}