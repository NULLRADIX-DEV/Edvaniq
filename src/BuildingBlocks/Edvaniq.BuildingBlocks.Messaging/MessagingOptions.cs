namespace Edvaniq.BuildingBlocks.Messaging;

// Settings of the messaging of a service. Everything but Service can be overridden in the section "Messaging" of the
// configuration, for example Messaging:StreamMaxBytes.
public sealed class MessagingOptions
{
    public const string Section = "Messaging";

    // The name of the service in lower case (planning). It names the stream of its events and the subjects below it.
    public string Service { get; set; } = "";

    // How often the dispatcher looks for events that are not sent yet, and how many it sends per round.
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 50;

    // How long the stream of the service keeps a message, and how many bytes it may hold before the oldest go.
    public TimeSpan StreamMaxAge { get; set; } = TimeSpan.FromDays(7);

    public long StreamMaxBytes { get; set; } = 256L * 1024 * 1024;

    // How long a consumer may take for a message before the broker delivers it again.
    public TimeSpan AckWait { get; set; } = TimeSpan.FromSeconds(30);

    // After this time sent outbox rows and inbox rows are deleted.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(14);
}