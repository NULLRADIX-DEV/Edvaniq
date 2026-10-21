using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Sender.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// The outbox against a real MySQL, with the same driver and settings as the services. The broker is the one inside the
// process, and the tests run the rounds of the dispatcher by hand.
public sealed class OutboxTests(MySqlTestServer mysql) : IAsyncLifetime
{
    private readonly InMemoryTransport transport = new() { DropsDuplicates = false };
    private TestService<SenderDbContext> sender = null!;

    public async ValueTask InitializeAsync() =>
        sender = TestService<SenderDbContext>.Create(
            "sender",
            await mysql.CreateDatabaseAsync("outbox"),
            configure: services => services.AddInMemoryTransport(transport),
            options: messaging => messaging.BatchSize = 10);

    public ValueTask DisposeAsync() => sender.DisposeAsync();

    [Fact]
    public void SaveChanges_StoresTheChangeAndItsEvent()
    {
        var id = CreateDocument("Skript Kapitel 1");

        Assert.Equal(id, sender.Query(db => db.Documents.Single()).Id);
        var message = sender.Query(db => db.Set<OutboxMessage>().Single());
        Assert.Equal("sender.greeted", message.Subject);
        Assert.Contains(id.ToString(), message.Payload);
        Assert.Null(message.SentAt);
    }

    // The duplicate title fails the whole SaveChanges, including the valid document before it.
    [Fact]
    public void FailedSaveChanges_StoresNeitherChangeNorEvent()
    {
        CreateDocument("Skript Kapitel 1");

        using (var scope = sender.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SenderDbContext>();
            var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
            AddDocument(db, outbox, "Skript Kapitel 2");
            AddDocument(db, outbox, "Skript Kapitel 1");
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        Assert.Equal(["Skript Kapitel 1"], sender.Query(db => db.Documents.Select(d => d.Title).ToList()));
        Assert.Equal(1, sender.Query(db => db.Set<OutboxMessage>().Count()));
    }

    [Fact]
    public void RolledBackTransaction_StoresNeitherChangeNorEvent()
    {
        using (var scope = sender.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SenderDbContext>();
            var outbox = scope.ServiceProvider.GetRequiredService<IEventOutbox>();
            using var transaction = db.Database.BeginTransaction();
            AddDocument(db, outbox, "Skript Kapitel 1");
            db.SaveChanges();
            AddDocument(db, outbox, "Skript Kapitel 2");
            db.SaveChanges();
            transaction.Rollback();
        }

        Assert.Equal(0, sender.Query(db => db.Documents.Count()));
        Assert.Equal(0, sender.Query(db => db.Set<OutboxMessage>().Count()));
    }

    [Fact]
    public async Task EventOfAnOpenTransaction_IsNotSent()
    {
        using var scope = sender.Scope();
        var db = scope.ServiceProvider.GetRequiredService<SenderDbContext>();
        using var transaction = db.Database.BeginTransaction();
        AddDocument(db, scope.ServiceProvider.GetRequiredService<IEventOutbox>(), "Skript Kapitel 1");
        db.SaveChanges();

        await DispatchAllAsync();

        Assert.Empty(transport.Published);
        transaction.Rollback();
    }

    // The process died between the commit and the send.
    [Fact]
    public async Task EventStoredBeforeACrash_IsSentByTheNextDispatcher()
    {
        CreateDocument("Skript Kapitel 1");

        await DispatchAllAsync();
        await DispatchAllAsync();

        var message = sender.Query(db => db.Set<OutboxMessage>().Single());
        var sent = Assert.Single(transport.Published);
        Assert.Equal(message.MessageId, sent.MessageId);
        Assert.Equal(message.Subject, sent.Subject);
        Assert.Equal(message.Payload, sent.Payload);
        Assert.NotNull(message.SentAt);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public async Task FailedSend_StaysInTheOutbox_AndIsSentLater()
    {
        CreateDocument("Skript Kapitel 1");
        transport.Failing = true;

        var failed = await sender.Dispatcher.DispatchOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new OutboxDispatcher.Round(0, true), failed);
        var waiting = sender.Query(db => db.Set<OutboxMessage>().Single());
        Assert.Null(waiting.SentAt);
        Assert.Equal(1, waiting.Attempts);
        Assert.Contains("failing", waiting.LastError);

        transport.Failing = false;
        await DispatchAllAsync();

        var sent = sender.Query(db => db.Set<OutboxMessage>().Single());
        Assert.NotNull(sent.SentAt);
        Assert.Equal(2, sent.Attempts);
        Assert.Null(sent.LastError);
        Assert.Equal(sent.MessageId, Assert.Single(transport.Published).MessageId);
    }

    // The broker is probably down for all of them, so the first failure ends the round and the order stays as it was.
    [Fact]
    public async Task FailedSend_EndsTheRound_AndLeavesTheLaterEventsUntouched()
    {
        CreateDocument("Skript Kapitel 1");
        CreateDocument("Skript Kapitel 2");
        CreateDocument("Skript Kapitel 3");
        transport.Failing = true;

        await sender.Dispatcher.DispatchOnceAsync(TestContext.Current.CancellationToken);

        var attempts = sender.Query(db => db.Set<OutboxMessage>().OrderBy(m => m.Id).Select(m => m.Attempts).ToList());
        Assert.Equal([1, 0, 0], attempts);
    }

    [Fact]
    public async Task ParallelDispatchers_SendEveryEventExactlyOnce()
    {
        sender.Save((db, outbox) =>
        {
            for (var index = 0; index < 300; index++)
            {
                AddDocument(db, outbox, $"Dokument {index}");
            }
        });

        // The transport keeps duplicates here, so a message sent twice would show.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DispatchAllAsync, TestContext.Current.CancellationToken)));

        Assert.Equal(0, sender.Query(db => db.Set<OutboxMessage>().Count(m => m.SentAt == null)));
        Assert.Equal(300, transport.Published.Count);
        Assert.Equal(300, transport.Published.Select(m => m.MessageId).Distinct().Count());
    }

    [Fact]
    public void Cleanup_DeletesOldSentEventsAndOldInboxEntries_ButKeepsTheRest()
    {
        var now = DateTime.UtcNow;
        sender.Save((db, _) =>
        {
            db.Set<OutboxMessage>().AddRange(
                OutboxRow("old and sent", sentAt: now.AddDays(-15)),
                OutboxRow("recent and sent", sentAt: now.AddDays(-1)),
                OutboxRow("old and unsent", sentAt: null));
            db.Set<InboxMessage>().AddRange(
                new InboxMessage { MessageId = Guid.NewGuid(), Consumer = "sender-old", ReceivedAt = now.AddDays(-15) },
                new InboxMessage { MessageId = Guid.NewGuid(), Consumer = "sender-recent", ReceivedAt = now.AddDays(-1) });
        });

        var deleted = sender.Cleanup.RunOnce();

        Assert.Equal((1, 1), deleted);
        Assert.Equal(["old and unsent", "recent and sent"], sender.Query(db => db.Set<OutboxMessage>().Select(m => m.Subject).OrderBy(s => s).ToList()));
        Assert.Equal(["sender-recent"], sender.Query(db => db.Set<InboxMessage>().Select(m => m.Consumer).ToList()));
    }

    private Guid CreateDocument(string title)
    {
        var id = Guid.Empty;
        sender.Save((db, outbox) => id = AddDocument(db, outbox, title));
        return id;
    }

    // The change and its event, the way a use case adds them. Storing them is up to the caller's SaveChanges.
    private static Guid AddDocument(SenderDbContext db, IEventOutbox outbox, string title)
    {
        var document = new Document { Id = Guid.NewGuid(), Title = title };
        db.Documents.Add(document);
        outbox.Add(new Greeted(document.Id, title));
        return document.Id;
    }

    private static OutboxMessage OutboxRow(string subject, DateTime? sentAt) =>
        new() { MessageId = Guid.NewGuid(), Subject = subject, Payload = "{}", OccurredAt = DateTime.UtcNow.AddDays(-30), SentAt = sentAt };

    // Stops once a round sends nothing, so an event that keeps failing does not spin forever.
    private async Task DispatchAllAsync()
    {
        while ((await sender.Dispatcher.DispatchOnceAsync(TestContext.Current.CancellationToken)).Sent > 0)
        {
        }
    }
}