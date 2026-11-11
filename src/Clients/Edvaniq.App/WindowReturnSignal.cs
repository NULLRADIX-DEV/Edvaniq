using Edvaniq.Client.Core;

namespace Edvaniq.App;

// The return in the app: its window is active again or the app is back in front. Leaving fires Deactivated and Stopped,
// coming back Activated and Resumed, so only the step from away to back counts.
public sealed class WindowReturnSignal : IReturnSignal
{
    private readonly Lock sync = new();
    private readonly List<Action> listeners = [];
    private bool away;

    public void Watch(Window window)
    {
        window.Deactivated += (_, _) => Leave();
        window.Stopped += (_, _) => Leave();
        window.Activated += (_, _) => Back();
        window.Resumed += (_, _) => Back();
    }

    public ValueTask<IAsyncDisposable> SubscribeAsync(Action onReturn)
    {
        lock (sync)
        {
            listeners.Add(onReturn);
        }

        return ValueTask.FromResult<IAsyncDisposable>(new Subscription(this, onReturn));
    }

    private void Leave()
    {
        lock (sync)
        {
            away = true;
        }
    }

    private void Back()
    {
        Action[] now;
        lock (sync)
        {
            if (!away)
            {
                return;
            }

            away = false;
            now = [.. listeners];
        }

        foreach (var listener in now)
        {
            listener();
        }
    }

    private void Unsubscribe(Action onReturn)
    {
        lock (sync)
        {
            listeners.Remove(onReturn);
        }
    }

    private sealed class Subscription(WindowReturnSignal signal, Action onReturn) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            signal.Unsubscribe(onReturn);
            return ValueTask.CompletedTask;
        }
    }
}