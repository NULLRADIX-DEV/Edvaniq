using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// One page in a browser context of its own. Remembers what the browser logged, so a shell that did not start says why.
public sealed class ShellPage : IAsyncDisposable
{
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
    public async Task LoadAsync(string url)
    {
        await Page.GotoAsync(url);
        await WaitUntilInteractiveAsync();
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

    private List<string> Log()
    {
        lock (log)
        {
            return [.. log];
        }
    }
}