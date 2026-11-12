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

    // How often a message whose handler says it isn't ready (MessageNotReadyException) is delivered before it is dropped.
    // The broker waits 5 seconds between two deliveries, so 360 is about half an hour: enough for a sender's event that
    // is still in its outbox while the sender or the broker restarts.
    public int NotReadyDeliveries { get; set; } = 360;

    // How long a consumer may fail at the same message before /health turns unhealthy and the server alarms. A failure
    // that heals by itself (a short outage of the database, a deploy of the sender) stays below it.
    public TimeSpan FailureAlarmAfter { get; set; } = TimeSpan.FromMinutes(5);

    // After this time sent outbox rows and inbox rows are deleted.
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(14);

    // How long a service remembers a deleted user (DeletedUser). Longer than any event can come late: the stream keeps
    // it 7 days, the inbox 14, an unsent one waits in the outbox. Backups of the user's data are gone after about as long.
    public TimeSpan DeletedUserRetention { get; set; } = TimeSpan.FromDays(30);

    // How long after a deletion the sweep looks once more for rows of the user. A handler that checked before the
    // deletion was stored commits within seconds, or the broker delivers its message again after AckWait.
    public TimeSpan DeletedUserSweepAfter { get; set; } = TimeSpan.FromMinutes(2);
}