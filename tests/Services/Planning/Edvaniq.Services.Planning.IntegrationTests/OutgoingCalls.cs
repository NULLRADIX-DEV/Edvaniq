using System.Collections.Concurrent;

namespace Edvaniq.Services.Planning.IntegrationTests;

// Counts the calls the service makes to other services, per token, and how many were open at once. With
// WaitForEachOther a call only answers once a second one with the same token is open (or after a while), so a plan
// that asks one after the other shows 1 at once and takes long, one that asks side by side shows 2.
public sealed class OutgoingCalls
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, Gauge> gauges = new();
    private readonly ConcurrentDictionary<string, bool> waiting = new();

    public void WaitForEachOther(string token) => waiting[token] = true;

    public int Total(string token) => gauges.TryGetValue(token, out var gauge) ? gauge.Total : 0;

    public int MostAtOnce(string token) => gauges.TryGetValue(token, out var gauge) ? gauge.MostAtOnce : 0;

    public async Task<IDisposable> EnterAsync(string? token, CancellationToken cancellationToken)
    {
        if (token is null)
        {
            return new Leave(null);
        }

        var gauge = gauges.GetOrAdd(token, _ => new Gauge());
        var open = gauge.Enter();
        if (waiting.ContainsKey(token) && open < 2)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Patience);
            try
            {
                await gauge.Second.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return new Leave(gauge);
    }

    private sealed class Gauge
    {
        private int open;
        private int total;
        private int mostAtOnce;

        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Total => Volatile.Read(ref total);

        public int MostAtOnce => Volatile.Read(ref mostAtOnce);

        public int Enter()
        {
            Interlocked.Increment(ref total);
            var now = Interlocked.Increment(ref open);
            int seen;
            while (now > (seen = Volatile.Read(ref mostAtOnce)) && Interlocked.CompareExchange(ref mostAtOnce, now, seen) != seen)
            {
            }

            if (now >= 2)
            {
                Second.TrySetResult();
            }

            return now;
        }

        public void Exit() => Interlocked.Decrement(ref open);
    }

    private sealed class Leave(Gauge? gauge) : IDisposable
    {
        public void Dispose() => gauge?.Exit();
    }
}