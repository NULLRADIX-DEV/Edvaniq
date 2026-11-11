using Edvaniq.Client.Core;

using Microsoft.JSInterop;

namespace Edvaniq.Client.UI.Shell;

// The return in the browser, from the script return-signal.js. It listens only while somebody subscribed, the first
// subscription starts it after the page has rendered, the last one stops it again.
public sealed class BrowserReturnSignal(IJSRuntime js) : IReturnSignal
{
    public const string Module = "./_content/Edvaniq.Client.UI/return-signal.js";

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock sync = new();
    private readonly List<Action> listeners = [];
    private IJSObjectReference? module;
    private DotNetObjectReference<BrowserReturnSignal>? self;

    public async ValueTask<IAsyncDisposable> SubscribeAsync(Action onReturn)
    {
        await gate.WaitAsync();
        try
        {
            if (module is null)
            {
                self = DotNetObjectReference.Create(this);
                try
                {
                    module = await js.InvokeAsync<IJSObjectReference>("import", Module);
                    await module.InvokeVoidAsync("start", self);
                }
                catch
                {
                    await ReleaseAsync();
                    throw;
                }
            }

            lock (sync)
            {
                listeners.Add(onReturn);
            }
        }
        finally
        {
            gate.Release();
        }

        return new Subscription(this, onReturn);
    }

    [JSInvokable]
    public void Returned()
    {
        Action[] now;
        lock (sync)
        {
            now = [.. listeners];
        }

        foreach (var listener in now)
        {
            listener();
        }
    }

    private async ValueTask UnsubscribeAsync(Action onReturn)
    {
        await gate.WaitAsync();
        try
        {
            lock (sync)
            {
                if (!listeners.Remove(onReturn) || listeners.Count > 0)
                {
                    return;
                }
            }

            if (module is not null)
            {
                try
                {
                    await module.InvokeVoidAsync("stop");
                }
                catch (JSDisconnectedException)
                {
                    // The page is gone, and its listeners with it.
                }
            }

            await ReleaseAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask ReleaseAsync()
    {
        if (module is not null)
        {
            try
            {
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        module = null;
        self?.Dispose();
        self = null;
    }

    private sealed class Subscription(BrowserReturnSignal signal, Action onReturn) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref disposed, 1) == 0 ? signal.UnsubscribeAsync(onReturn) : ValueTask.CompletedTask;
    }
}