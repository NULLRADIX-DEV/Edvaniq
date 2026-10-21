using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Spikes.Outbox;

// Real MySQL with Oracle's provider, same as the services.
public sealed class OutboxTests(MySqlTestServer mysql) : IAsyncLifetime
{
    private readonly FakeTransport transport = new();
    private string connectionString = "";

    public async ValueTask InitializeAsync()
    {
        connectionString = await mysql.CreateDatabaseAsync("outbox");
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void SaveChanges_StoresTheChangeAndItsEvent()
    {
        var id = CreateDocument("Skript Kapitel 1");

        using var context = CreateContext();
        Assert.Equal(id, context.Documents.Single().Id);
        var message = context.Outbox.Single();
        Assert.Equal(nameof(DocumentCreated), message.Type);
        Assert.Contains(id.ToString(), message.Payload);
        Assert.Null(message.SentAt);
    }

    // The duplicate title fails the whole SaveChanges, including the valid document before it.
    [Fact]
    public void FailedSaveChanges_StoresNeitherChangeNorEvent()
    {
        CreateDocument("Skript Kapitel 1");

        using (var context = CreateContext())
        {
            AddDocument(context, "Skript Kapitel 2");
            AddDocument(context, "Skript Kapitel 1");
            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        using var check = CreateContext();
        Assert.Equal(["Skript Kapitel 1"], check.Documents.Select(d => d.Title).ToList());
        Assert.Equal(1, check.Outbox.Count());
    }

    [Fact]
    public void RolledBackTransaction_StoresNeitherChangeNorEvent()
    {
        using (var context = CreateContext())
        {
            using var transaction = context.Database.BeginTransaction();
            AddDocument(context, "Skript Kapitel 1");
            context.SaveChanges();
            AddDocument(context, "Skript Kapitel 2");
            context.SaveChanges();
            transaction.Rollback();
        }

        using var check = CreateContext();
        Assert.Equal(0, check.Documents.Count());
        Assert.Equal(0, check.Outbox.Count());
    }

    [Fact]
    public void EventOfAnOpenTransaction_IsNotSent()
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();
        AddDocument(context, "Skript Kapitel 1");
        context.SaveChanges();

        Dispatcher().DispatchAll();

        Assert.Empty(transport.Received);
        transaction.Rollback();
    }

    // Process died between commit and send.
    [Fact]
    public void EventStoredBeforeACrash_IsSentByTheNextDispatcher()
    {
        CreateDocument("Skript Kapitel 1");

        Dispatcher().DispatchAll();
        Dispatcher().DispatchAll();

        using var context = CreateContext();
        var message = context.Outbox.Single();
        Assert.Equal(1, transport.Received[message.MessageId]);
        Assert.NotNull(message.SentAt);
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public void FailedSend_StaysInTheOutbox_AndIsSentLater()
    {
        CreateDocument("Skript Kapitel 1");
        transport.Failing = true;

        Dispatcher().DispatchAll();

        using (var context = CreateContext())
        {
            var waiting = context.Outbox.Single();
            Assert.Null(waiting.SentAt);
            Assert.Equal(1, waiting.Attempts);
            Assert.Equal("Broker unreachable", waiting.LastError);
        }

        transport.Failing = false;
        Dispatcher().DispatchAll();

        using var check = CreateContext();
        var sent = check.Outbox.Single();
        Assert.NotNull(sent.SentAt);
        Assert.Equal(2, sent.Attempts);
        Assert.Null(sent.LastError);
        Assert.Equal(1, transport.Received[sent.MessageId]);
    }

    [Fact]
    public void ParallelDispatchers_SendEveryEventExactlyOnce()
    {
        using (var context = CreateContext())
        {
            for (var index = 0; index < 300; index++)
            {
                AddDocument(context, $"Dokument {index}");
            }

            context.SaveChanges();
        }

        Parallel.For(0, 4, _ => Dispatcher().DispatchAll(batchSize: 10));

        using var check = CreateContext();
        Assert.Equal(0, check.Outbox.Count(m => m.SentAt == null));
        Assert.Equal(300, transport.Received.Count);
        Assert.All(transport.Received.Values, count => Assert.Equal(1, count));
    }

    private Guid CreateDocument(string title)
    {
        using var context = CreateContext();
        var id = AddDocument(context, title);
        context.SaveChanges();
        return id;
    }

    private static Guid AddDocument(SpikeDbContext context, string title)
    {
        var document = new Document { Id = Guid.NewGuid(), Title = title };
        context.Documents.Add(document);
        context.Enqueue(new DocumentCreated(document.Id, title));
        return document.Id;
    }

    private OutboxDispatcher Dispatcher() => new(CreateContext, transport);

    private SpikeDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SpikeDbContext>()
            .UseMySQL(MySqlConnections.For(connectionString).ConnectionString)
            .Options);
}