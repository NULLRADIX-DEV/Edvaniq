namespace Edvaniq.Client.UI.Pages;

// How often the topics page asks for the state of the newest text, and for how long. An analysis takes from seconds to
// minutes. Tests register shorter times.
public sealed record MaterialPolling(TimeSpan Interval, TimeSpan SlowInterval, TimeSpan SlowAfter, TimeSpan AnalysisLimit)
{
    public static readonly MaterialPolling Default = new(
        Interval: TimeSpan.FromSeconds(2),
        SlowInterval: TimeSpan.FromSeconds(5),
        SlowAfter: TimeSpan.FromMinutes(1),
        AnalysisLimit: TimeSpan.FromMinutes(10));

    public TimeSpan Wait(TimeSpan waited) => waited < SlowAfter ? Interval : SlowInterval;
}