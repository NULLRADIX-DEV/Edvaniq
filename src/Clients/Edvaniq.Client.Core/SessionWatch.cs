using System.Net;

namespace Edvaniq.Client.Core;

// Notices when the login runs out or ends elsewhere: the web host then answers a call under /api with 401. One per app,
// the shell shows the hint, the pages keep what was not sent yet. Not for the host itself, there it would be shared by
// every user.
public sealed class SessionWatch
{
    public bool Ended { get; private set; }

    public event Action? Changed;

    public void Report() => Set(true);

    // After a new login or a logout there is nothing to point out any more.
    public void Reset() => Set(false);

    private void Set(bool ended)
    {
        if (Ended == ended)
        {
            return;
        }

        Ended = ended;
        Changed?.Invoke();
    }
}

// Sits in front of every HttpClient that calls the web host and tells the watch about a 401 under /api. A 401 on
// /session means something else: nobody signed in, or a wrong password.
public sealed class SessionWatchHandler(SessionWatch watch) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && IsApi(request.RequestUri))
        {
            watch.Report();
        }

        return response;
    }

    private static bool IsApi(Uri? uri) =>
        uri is { IsAbsoluteUri: true } && uri.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);
}