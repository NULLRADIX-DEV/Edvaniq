namespace Edvaniq.Client.Core;

// Tells when the user comes back: the tab or window has the focus again, the app is in front again, or in the browser the
// connection is back. Pages then fetch
// the server's state anew, it may have changed on another device meanwhile. One call per return, however many ways the
// platform reports it.
public interface IReturnSignal
{
    // Listens from the first subscription until the last one is disposed. The callback runs on whatever thread the
    // platform reports on.
    ValueTask<IAsyncDisposable> SubscribeAsync(Action onReturn);
}