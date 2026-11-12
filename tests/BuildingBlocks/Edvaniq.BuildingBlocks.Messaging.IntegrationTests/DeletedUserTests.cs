using System.Text.Json;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Sender.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// #944 against a real MySQL: once a user is deleted, a late event of theirs is confirmed without effect, and the sweep
// removes what a handler stored while the deletion ran.
public sealed class DeletedUserTests(MySqlTestServer mysql) : IAsyncLifetime
{
    private const string Consumer = "notes-NoteWrittenHandler";

    private readonly InMemoryTransport transport = new();
    private string connectionString = "";
    private TestService<NotesDbContext>? notes;

    public async ValueTask InitializeAsync() => connectionString = await mysql.CreateDatabaseAsync("deletedusers");

    public async ValueTask DisposeAsync()
    {
        if (notes is not null)
        {
            await notes.DisposeAsync();
        }
    }

    [Fact]
    public async Task LateEvent_OfADeletedUser_IsConfirmedWithoutEffect_OthersStillWork()
    {
        await StartAsync();
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        Delete(alice);

        var late = await PublishAsync(alice);
        var other = await PublishAsync(bob);

        await Wait.UntilAsync(() => Inbox(late) && Inbox(other), "The events were not confirmed.", () => notes!.Logs);
        Assert.Equal(0, NotesOf(alice));
        Assert.Equal(1, NotesOf(bob));
        Assert.Contains($"Consumer {Consumer} skipped NoteWritten of a deleted user", notes!.Logs);
        Assert.DoesNotContain(alice, notes.Logs);
    }

    // The user is only stored as a hash.
    [Fact]
    public async Task Deletion_RemembersTheUserOnce_AndOnlyAsAHash()
    {
        await StartAsync();
        var alice = NewUser("alice");

        Delete(alice);
        Delete(alice);

        var hash = Assert.Single(notes!.Query(db => db.Set<DeletedUser>().Select(user => user.Hash).ToList()));
        Assert.Equal(DeletedUser.HashOf(alice), hash);
        Assert.DoesNotContain(alice, hash, StringComparison.Ordinal);
        Assert.NotEqual(DeletedUser.HashOf(alice.ToUpperInvariant()), hash);
    }

    // A handler that checked before the deletion was stored saves its note afterwards. The sweep removes it.
    [Fact]
    public async Task Sweep_RemovesRowsStoredAfterTheDeletion_AndLooksOnlyOnce()
    {
        await StartAsync(messaging => messaging.DeletedUserSweepAfter = TimeSpan.Zero);
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        AddNote(bob);
        Delete(alice);
        AddNote(alice);

        Assert.Equal(1, await Sweep.RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, NotesOf(alice));
        Assert.Equal(1, NotesOf(bob));
        Assert.True(notes!.Query(db => db.Set<DeletedUser>().Single().Swept));
        Assert.Equal(0, await Sweep.RunOnceAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sweep_WaitsForTheDelay()
    {
        await StartAsync();
        var alice = NewUser("alice");
        Delete(alice);
        AddNote(alice);

        Assert.Equal(0, await Sweep.RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, NotesOf(alice));
        Assert.False(notes!.Query(db => db.Set<DeletedUser>().Single().Swept));
    }

    private DeletedUserSweep<NotesDbContext> Sweep =>
        notes!.Host.Services.GetServices<IHostedService>().OfType<DeletedUserSweep<NotesDbContext>>().Single();

    private async Task StartAsync(Action<MessagingOptions>? options = null)
    {
        notes = TestService<NotesDbContext>.Create(
            "notes",
            connectionString,
            options: options,
            configure: services =>
            {
                services.AddInMemoryTransport(transport);
                services.AddUserDeletion<NotesDbContext, NotesDeletion>();
                services.AddIntegrationEventHandler<NoteWritten, NoteWrittenHandler>();
            });
        await notes.StartAsync();
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    // What the UserDeleted handler does.
    private void Delete(string user)
    {
        using var scope = notes!.Scope();
        scope.ServiceProvider.GetRequiredService<IUserDataRemover>().RemoveAll(user);
        scope.ServiceProvider.GetRequiredService<NotesDbContext>().SaveChanges();
    }

    private void AddNote(string user)
    {
        using var scope = notes!.Scope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        db.Notes.Add(new Note { Id = Guid.NewGuid(), OwnerId = user });
        db.SaveChanges();
    }

    private async Task<Guid> PublishAsync(string user)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new NoteWritten(user, Guid.NewGuid()), EventJson.Options);
        await transport.PublishAsync(new OutgoingMessage(EventRoute.For(typeof(NoteWritten)).Subject, id, payload, null), TestContext.Current.CancellationToken);
        return id;
    }

    private bool Inbox(Guid messageId) =>
        notes!.Query(db => db.Set<InboxMessage>().Any(m => m.MessageId == messageId && m.Consumer == Consumer));

    private int NotesOf(string user) => notes!.Query(db => db.Notes.IgnoreQueryFilters().Count(note => note.OwnerId == user));
}

public sealed class NoteWrittenHandler(NotesDbContext db) : IIntegrationEventHandler<NoteWritten>
{
    public Task HandleAsync(NoteWritten integrationEvent, CancellationToken cancellationToken)
    {
        db.Notes.Add(new Note { Id = integrationEvent.NoteId, OwnerId = integrationEvent.UserId });
        return Task.CompletedTask;
    }
}