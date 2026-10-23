using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// What the handler of a deleted user removes, against a real MySQL: the rows of every entity with IOwnedByUser, also those
// a filter hides, those of a derived type and those that hang on them without an OwnerId. Nobody else loses anything.
public sealed class UserDataRemoverTests(MySqlTestServer mysql) : IAsyncLifetime
{
    private TestService<NotesDbContext>? service;

    public async ValueTask InitializeAsync() =>
        service = TestService<NotesDbContext>.Create(
            "notes",
            await mysql.CreateDatabaseAsync("notes"),
            configure: services => services.AddUserDataRemover<NotesDbContext>());

    public async ValueTask DisposeAsync() => await service!.DisposeAsync();

    [Fact]
    public void RemoveAll_RemovesEveryRowOfTheUser_AndNothingElse()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        Seed(alice);
        Seed(bob);

        var removed = Remove(alice, save: true);

        // Two notes, one of them hidden, and two entries, one of them archived. The lines go with their notes.
        Assert.Equal(4, removed);
        Assert.Equal(new Rows(0, 0, 0), RowsOf(alice));
        Assert.Equal(new Rows(2, 2, 2), RowsOf(bob));
    }

    // The consumer stores the removal together with its inbox entry, so the remover itself stores nothing.
    [Fact]
    public void RemoveAll_StoresNothingByItself()
    {
        var alice = NewUser("alice");
        Seed(alice);

        Remove(alice, save: false);

        Assert.Equal(new Rows(2, 2, 2), RowsOf(alice));
    }

    // A second delivery of the same deletion finds nothing left.
    [Fact]
    public void RemoveAll_OfAUserWithoutData_RemovesNothing()
    {
        var bob = NewUser("bob");
        Seed(bob);

        Assert.Equal(0, Remove(NewUser("alice"), save: true));
        Assert.Equal(new Rows(2, 2, 2), RowsOf(bob));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private void Seed(string user)
    {
        using var scope = service!.Scope();
        var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
        db.Notes.Add(new Note { Id = Guid.NewGuid(), OwnerId = user, Lines = [new NoteLine { Id = Guid.NewGuid() }] });
        db.Notes.Add(new Note { Id = Guid.NewGuid(), OwnerId = user, Hidden = true, Lines = [new NoteLine { Id = Guid.NewGuid() }] });
        db.Entries.Add(new Entry { Id = Guid.NewGuid(), OwnerId = user });
        db.Entries.Add(new ArchivedEntry { Id = Guid.NewGuid(), OwnerId = user });
        db.SaveChanges();
    }

    private int Remove(string user, bool save)
    {
        using var scope = service!.Scope();
        var removed = scope.ServiceProvider.GetRequiredService<IUserDataRemover>().RemoveAll(user);
        if (save)
        {
            scope.ServiceProvider.GetRequiredService<NotesDbContext>().SaveChanges();
        }

        return removed;
    }

    private Rows RowsOf(string user) =>
        service!.Query(db => new Rows(
            db.Notes.IgnoreQueryFilters().Count(note => note.OwnerId == user),
            db.Set<NoteLine>().IgnoreQueryFilters().Count(line => line.Note.OwnerId == user),
            db.Entries.IgnoreQueryFilters().Count(entry => entry.OwnerId == user)));

    private sealed record Rows(int Notes, int Lines, int Entries);
}

// A service with data of users in the shapes a real one may have. A hidden note is behind a filter of its own.
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options) : ServiceDbContext(options, new NoUser())
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Entry> Entries => Set<Entry>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>().HasQueryFilter("Visible", note => !note.Hidden);
        modelBuilder.Entity<Note>().HasMany(note => note.Lines).WithOne(line => line.Note).IsRequired();
        modelBuilder.Entity<ArchivedEntry>();
    }
}

public sealed class Note : IOwnedByUser
{
    public Guid Id { get; set; }

    public required string OwnerId { get; set; }

    public bool Hidden { get; set; }

    public List<NoteLine> Lines { get; set; } = [];
}

// Belongs to a user only through its note.
public sealed class NoteLine
{
    public Guid Id { get; set; }

    public Note Note { get; set; } = null!;
}

public class Entry : IOwnedByUser
{
    public Guid Id { get; set; }

    public required string OwnerId { get; set; }
}

// In the table of Entry (TPH): removed with it, not a second time.
public sealed class ArchivedEntry : Entry;