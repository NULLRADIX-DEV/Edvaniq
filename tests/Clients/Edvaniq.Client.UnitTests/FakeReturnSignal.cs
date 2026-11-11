using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// A return the test triggers by hand.
internal sealed class FakeReturnSignal : IReturnSignal
{
    private readonly List<Action> listeners = [];

    public int Subscribers => listeners.Count;

    public ValueTask<IAsyncDisposable> SubscribeAsync(Action onReturn)
    {
        listeners.Add(onReturn);
        return ValueTask.FromResult<IAsyncDisposable>(new Subscription(listeners, onReturn));
    }

    public void Return()
    {
        foreach (var listener in listeners.ToArray())
        {
            listener();
        }
    }

    private sealed class Subscription(List<Action> listeners, Action onReturn) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            listeners.Remove(onReturn);
            return ValueTask.CompletedTask;
        }
    }
}