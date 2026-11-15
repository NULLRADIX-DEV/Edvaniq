namespace Edvaniq.Client.UI.Pages;

// How often the summary page asks while a summary is made, and when it stops asking by itself. A summary takes seconds
// up to about a minute. Longer means the AI is away and Content tries again after 30 s, 2 and 8 minutes, so asking every
// 2 s after 3 minutes would only cost calls; the page offers to check again instead.
public sealed record SummaryPolling(TimeSpan Interval, TimeSpan Limit)
{
    public static readonly SummaryPolling Default = new(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(3));
}