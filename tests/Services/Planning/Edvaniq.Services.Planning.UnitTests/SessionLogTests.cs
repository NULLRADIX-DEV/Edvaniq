using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Testing;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.UnitTests;

// #417: the end of a session in the log, with its id, outcome and duration, and the cases that go wrong: a second end,
// a change stored at the same time and one that never gets stored. Ids only, never the user.
public sealed class SessionLogTests
{
    private const string User = "alice-5f0c4a3e1b2d4c5e8f9a0b1c2d3e4f50";

    private static readonly Guid Goal = new("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid Topic = new("00000000-0000-0000-0000-00000000000a");
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateTime Started = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DayBlock Block = DayBlocks.WithIds(User, Goal, Today, [new Block(Topic, 15)])[0];
    private static readonly Guid Id = Guid.NewGuid();

    private readonly TestLogs logs = new();

    [Fact]
    public async Task Complete_LogsTheSession_WithItsDuration()
    {
        var changes = Changes(new FakeSessions(), minutesLater: 4);

        await changes.CompleteAsync(Id, TestContext.Current.CancellationToken);

        var entry = Single("SessionEnded", LogLevel.Information);
        Assert.Equal($"Session {Id} ended completed after 240 s, 240 s of active learning", entry.Message);
        Assert.Empty(logs.Containing(User));
    }

    [Fact]
    public async Task Abort_LogsTheSession_AndASecondEnd_SaysItEndedAlready()
    {
        var sessions = new FakeSessions();
        var changes = Changes(sessions, minutesLater: 2);

        await changes.AbortAsync(Id, TestContext.Current.CancellationToken);
        sessions.KeepOne = true;
        await changes.AbortAsync(Id, TestContext.Current.CancellationToken);
        await changes.CompleteAsync(Id, TestContext.Current.CancellationToken);

        Assert.StartsWith($"Session {Id} ended aborted after 120 s", Single("SessionEnded", LogLevel.Information).Message, StringComparison.Ordinal);
        var again = logs.Entries.Where(entry => entry.EventName == "SessionEndedAlready").ToList();
        Assert.Equal(2, again.Count);
        Assert.All(again, entry => Assert.Equal($"Session {Id} was aborted already, nothing sent again", entry.Message));
    }

    // Two finishes at once: the one stored second tries again on the session as stored.
    [Fact]
    public async Task ChangeStoredAtTheSameTime_IsTriedAgain_AndSaysSo()
    {
        var changes = Changes(new FakeSessions(saves: [false, true]), minutesLater: 4);

        await changes.CompleteAsync(Id, TestContext.Current.CancellationToken);

        Assert.Equal($"Session {Id} was changed at the same time, trying again (attempt 1)", Single("SessionChangeConflict", LogLevel.Information).Message);
        Single("SessionEnded", LogLevel.Information);
    }

    [Fact]
    public async Task ChangeNeverStored_IsAnError()
    {
        var changes = Changes(new FakeSessions(saves: [false, false, false]), minutesLater: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => changes.CompleteAsync(Id, TestContext.Current.CancellationToken));

        Assert.Equal($"Session {Id} kept changing, a change was not stored in 3 attempts", Single("SessionChangeGaveUp", LogLevel.Error).Message);
        Assert.Equal(2, logs.Entries.Count(entry => entry.EventName == "SessionChangeConflict"));
        Assert.DoesNotContain(logs.Entries, entry => entry.EventName == "SessionEnded");
    }

    private TestLogEntry Single(string eventName, LogLevel level)
    {
        var entry = Assert.Single(logs.Entries, entry => entry.EventName == eventName);
        Assert.Equal(level, entry.Level);
        return entry;
    }

    private SessionChanges Changes(FakeSessions sessions, int minutesLater)
    {
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        return new SessionChanges(
            sessions, new NoDoneBlocks(), new NoOutbox(), new FixedTime(Started.AddMinutes(minutesLater)), factory.CreateLogger<SessionChanges>());
    }

    // Each read gives the session as stored: a fresh one, or with KeepOne the one changed before. Saves answer in turn,
    // then true.
    private sealed class FakeSessions(IEnumerable<bool>? saves = null) : ISessions
    {
        private readonly Queue<bool> saves = new(saves ?? []);
        private Session? kept;

        public bool KeepOne { get; set; }

        public Task<Session?> FindForChangeAsync(Guid id, CancellationToken cancellationToken)
        {
            var session = KeepOne && kept is not null ? kept : Session.Start(id, User, Goal, Today, Block, Started, topicIsNew: true);
            kept = session;
            return Task.FromResult<Session?>(session);
        }

        public Task<bool> TrySaveAsync(CancellationToken cancellationToken) => Task.FromResult(saves.Count == 0 || saves.Dequeue());

        public Task<Session?> ActiveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        // No earlier session on the block, so no unit start to tell.
        public Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Session?>(null);

        public Task<Session?> LastAbortedAsync(Guid blockId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Session>> OnBlockAsync(Guid blockId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryAddAsync(Session session, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoDoneBlocks : IDoneBlocks
    {
        public Task<IReadOnlyList<DoneBlock>> OnAsync(Guid learningGoalId, DateOnly studyDay, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(DoneBlock block)
        {
        }
    }

    private sealed class NoOutbox : IEventOutbox
    {
        public void Add(IIntegrationEvent integrationEvent)
        {
        }
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}