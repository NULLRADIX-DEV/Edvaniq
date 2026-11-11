namespace Edvaniq.BuildingBlocks.Application;

// A handler throws it when the message can't be handled yet, because something it needs is still on its way. The broker
// delivers the message again a little later, but only Messaging:NotReadyDeliveries times in all. After that the message
// is dropped with a log line, so one that never fits doesn't come back forever. Any other exception keeps coming back.
public sealed class MessageNotReadyException(string reason) : Exception(reason);