namespace Edvaniq.Testing;

// The error a host fails with on start. WebApplicationFactory runs Program on a thread of its own, and when the start
// throws fast enough Program disposes the host before the factory looks at it: the test then sees an
// ObjectDisposedException instead of the real error. Starting again gives the real one.
public static class StartFailure
{
    private const int Attempts = 5;

    public static TException Of<TException>(Action start)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(start);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                start();
            }
            catch (TException exception)
            {
                return exception;
            }
            catch (ObjectDisposedException) when (attempt < Attempts)
            {
                continue;
            }

            throw new InvalidOperationException($"The host started, {typeof(TException).Name} was expected.");
        }
    }
}
