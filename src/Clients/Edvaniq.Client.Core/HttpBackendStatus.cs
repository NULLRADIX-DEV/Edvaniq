namespace Edvaniq.Client.Core;

// Asks the status endpoint of the web host. The HttpClient carries the base address of the host and a short timeout:
// a user waiting in front of an empty page should get the hint within seconds, not after the default 100.
public sealed class HttpBackendStatus(HttpClient http) : IBackendStatus
{
    public const string Path = "api/status";

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(Path, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}