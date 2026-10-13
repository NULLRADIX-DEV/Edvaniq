namespace Edvaniq.Testing;

// A clock that stands still until a test moves it. Starts at the real time, so tokens it stamps are valid elsewhere.
public sealed class TestTime : TimeProvider
{
    private long ticks = System.GetUtcNow().UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref ticks, by.Ticks);
}