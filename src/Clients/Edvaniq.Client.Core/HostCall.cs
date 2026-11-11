using System.Text.Json;

namespace Edvaniq.Client.Core;

// A call to the web host that didn't get through or brought no answer one can read ends as Failed. Only a cancel by the
// caller itself goes on as an exception.
internal static class HostCall
{
    public static async Task<CallOutcome<T>> GuardAsync<T>(Func<Task<CallOutcome<T>>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (HttpRequestException)
        {
            return CallOutcome<T>.Failed();
        }
        catch (JsonException)
        {
            return CallOutcome<T>.Failed();
        }
        catch (NotSupportedException)
        {
            // An answer that is no JSON at all, an HTML page from a proxy for instance.
            return CallOutcome<T>.Failed();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CallOutcome<T>.Failed();
        }
    }
}