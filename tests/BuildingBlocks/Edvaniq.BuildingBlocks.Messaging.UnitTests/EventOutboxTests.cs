using System.Text.Json;

using Edvaniq.Services.Alpha.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// Adding an event only puts a row into the DbContext of the request, the SaveChanges of the caller stores it with the change.
public class EventOutboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_PutsTheEventIntoTheChangeTrackerAndStoresNothing()
    {
        using var db = TestDbContext.Create();
        var pinged = new Pinged(Guid.NewGuid(), "Hallo");

        Outbox(db).Add(pinged);

        var message = Assert.Single(db.ChangeTracker.Entries<OutboxMessage>()).Entity;
        Assert.Equal(EntityState.Added, db.Entry(message).State);
        Assert.Equal("alpha.pinged", message.Subject);
        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal(Now.UtcDateTime, message.OccurredAt);
        Assert.Null(message.SentAt);
        Assert.Equal(0, message.Attempts);
        Assert.Equal(pinged, JsonSerializer.Deserialize<Pinged>(message.Payload, MessageJson.Options));
    }

    [Fact]
    public void Add_WritesTheEventAsCamelCaseJson()
    {
        using var db = TestDbContext.Create();
        var id = Guid.NewGuid();

        Outbox(db).Add(new Pinged(id, "Hallo"));

        Assert.Equal($$"""{"id":"{{id}}","text":"Hallo"}""", db.ChangeTracker.Entries<OutboxMessage>().Single().Entity.Payload);
    }

    [Fact]
    public void EveryEvent_GetsAMessageIdOfItsOwn()
    {
        using var db = TestDbContext.Create();
        var outbox = Outbox(db);

        outbox.Add(new Pinged(Guid.NewGuid(), "eins"));
        outbox.Add(new Pinged(Guid.NewGuid(), "zwei"));

        var ids = db.ChangeTracker.Entries<OutboxMessage>().Select(entry => entry.Entity.MessageId).ToList();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public void Add_RejectsTheEventOfAnotherService()
    {
        using var db = TestDbContext.Create();

        var exception = Assert.Throws<InvalidOperationException>(() => Outbox(db, service: "beta").Add(new Pinged(Guid.NewGuid(), "Hallo")));

        Assert.Contains("only sends its own events", exception.Message);
        Assert.Empty(db.ChangeTracker.Entries<OutboxMessage>());
    }

    [Fact]
    public void Add_RejectsATypeOutsideTheContractsOfAService()
    {
        using var db = TestDbContext.Create();

        Assert.Throws<InvalidOperationException>(() => Outbox(db).Add(new Stray(Guid.NewGuid())));
    }

    private static EventOutbox Outbox(TestDbContext db, string service = "alpha") =>
        new(db, new MessagingOptions { Service = service }, new FixedTime(Now));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}