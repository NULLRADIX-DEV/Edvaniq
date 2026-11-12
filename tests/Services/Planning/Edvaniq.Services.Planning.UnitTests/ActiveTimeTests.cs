using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

// #394: a gap between two signals up to the idle limit counts whole, a longer one not at all, and nothing counts before
// the start or after the end of the session.
public class ActiveTimeTests
{
    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateTime Now = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DayBlock Block = DayBlocks.WithIds("alice", Goal, Today, [new Block(Guid.NewGuid(), 15)])[0];

    public static TheoryData<int, int> Gaps => new()
    {
        { 0, 0 },
        { 1, 1 },
        { 299, 299 },
        { 300, 300 },
        { 301, 0 },
        { 1800, 0 },
    };

    [Theory]
    [MemberData(nameof(Gaps))]
    public void Gap_CountsWholeUpToTheLimit_OtherwiseNothing(int seconds, int counted)
    {
        var session = Start();

        session.RecordActivity(Now.AddSeconds(seconds));

        Assert.Equal(counted, session.ActiveSeconds);
        Assert.Equal(TimeSpan.FromSeconds(counted), ActiveTime.Between(Now, Now.AddSeconds(seconds)));
    }

    [Fact]
    public void IdleLimit_IsFiveMinutes() => Assert.Equal(TimeSpan.FromMinutes(5), ActiveTime.IdleLimit);

    // Start, +2 min, +10 min (idle), +1 min broken off: 3 min.
    [Fact]
    public void Signals_AddUpTheGapsUnderTheLimit()
    {
        var session = Start();

        session.Advance(1, Now.AddMinutes(2));
        session.RecordActivity(Now.AddMinutes(12));
        session.Abort(Now.AddMinutes(13));

        Assert.Equal(180, session.ActiveSeconds);
        Assert.Equal(Now.AddMinutes(13), session.LastActivityAt);
    }

    [Fact]
    public void AfterTheEnd_NothingCounts()
    {
        var session = Start();
        session.Abort(Now.AddMinutes(1));

        Assert.False(session.RecordActivity(Now.AddMinutes(2)));
        Assert.Equal(SessionAdvance.Ended, session.Advance(1, Now.AddMinutes(3)));
        Assert.False(session.Abort(Now.AddMinutes(4)));

        Assert.Equal(60, session.ActiveSeconds);
        Assert.Equal(Now.AddMinutes(1), session.LastActivityAt);
    }

    // A signal from before the last one (or before the start) counts nothing and doesn't move the last signal back.
    [Fact]
    public void SignalOutOfOrder_CountsNothing()
    {
        var session = Start();
        session.RecordActivity(Now.AddMinutes(2));

        session.RecordActivity(Now.AddMinutes(1));
        session.RecordActivity(Now.AddMinutes(-1));

        Assert.Equal(120, session.ActiveSeconds);
        Assert.Equal(Now.AddMinutes(2), session.LastActivityAt);
    }

    // A step out of range is no signal, the same step again is one.
    [Fact]
    public void Advance_CountsOnlyARealStep()
    {
        var session = Start();

        session.Advance(9, Now.AddMinutes(1));
        session.Advance(0, Now.AddMinutes(2));

        Assert.Equal(120, session.ActiveSeconds);
    }

    [Fact]
    public void NewSession_StartsAtZero_WithTheStartAsSignal()
    {
        var session = Start();

        Assert.Equal(0, session.ActiveSeconds);
        Assert.Equal(Now, session.LastActivityAt);
    }

    private static Session Start() => Session.Start(Guid.NewGuid(), "alice", Goal, Today, Block, Now);
}