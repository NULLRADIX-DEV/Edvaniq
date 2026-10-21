using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// One page in a browser context of its own. Remembers what the browser logged, so a shell that did not start says why.
public sealed class ShellPage : IAsyncDisposable
{
    private const int LoadAttempts = 3;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    private readonly IBrowserContext context;
    private readonly List<string> log = [];

    public ShellPage(IBrowserContext context, IPage page)
    {
        this.context = context;
        Page = page;
        page.Console += (_, message) => Remember($"console {message.Type}: {message.Text}");
        page.PageError += (_, error) => Remember($"page error: {error}");
        page.RequestFailed += (_, request) => Remember($"request failed: {request.Url} {request.Failure}");
    }

    public IPage Page { get; }

    // Opens the address and waits until the shell is interactive. Only then the prerendered page has turned into the
    // one the client runs, with the menu button, the check of the backend and everything else a visitor can use.
    // On the CI runner the integration tests create Docker networks at the same time. Chromium takes that as a network
    // change and drops the runtime's downloads (ERR_NETWORK_CHANGED), so such a load gets another go.
    public async Task LoadAsync(string url)
    {
        for (var attempt = 1; ; attempt++)
        {
            var logged = Log().Count;
            await Page.GotoAsync(url);
            try
            {
                await WaitUntilInteractiveAsync();
                return;
            }
            catch (InvalidOperationException) when (attempt < LoadAttempts && NetworkChangedSince(logged))
            {
            }
        }
    }

    public async Task WaitUntilInteractiveAsync()
    {
        try
        {
            // The button is only rendered once the runtime runs the shell. On a wide screen it stays hidden.
            await Page.Locator(".shell-menu-button").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = (float)StartTimeout.TotalMilliseconds,
            });
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                $"The shell at {Page.Url} did not become interactive within {StartTimeout.TotalSeconds} seconds. "
                + "What the browser logged:" + Environment.NewLine + string.Join(Environment.NewLine, Log()),
                exception);
        }

        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public ValueTask DisposeAsync() => context.DisposeAsync();

    private void Remember(string line)
    {
        lock (log)
        {
            log.Add(line);
        }
    }

    private bool NetworkChangedSince(int logged) =>
        Log().Skip(logged).Any(line => line.Contains("ERR_NETWORK_CHANGED", StringComparison.Ordinal));

    private List<string> Log()
    {
        lock (log)
        {
            return [.. log];
        }
    }
}