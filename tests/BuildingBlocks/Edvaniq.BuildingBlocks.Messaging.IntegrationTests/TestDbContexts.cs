using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// The DbContext of the sending service. ServiceDbContext adds the outbox and inbox tables. A document with a title that
// exists already fails the whole SaveChanges.
public sealed class SenderDbContext(DbContextOptions<SenderDbContext> options) : ServiceDbContext(options, new NoUser())
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Document>(document =>
        {
            document.Property(d => d.Title).HasMaxLength(200);
            document.HasIndex(d => d.Title).IsUnique();
        });
}

// The DbContext of the receiving service, with the table its handlers write to.
public sealed class ReceiverDbContext(DbContextOptions<ReceiverDbContext> options) : ServiceDbContext(options, new NoUser())
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Notification>().Property(n => n.Text).HasMaxLength(300);
}

public sealed class Document
{
    public Guid Id { get; set; }

    public required string Title { get; set; }
}

public sealed class Notification
{
    public Guid Id { get; set; }

    public Guid GreetingId { get; set; }

    public required string Text { get; set; }
}

internal sealed class NoUser : ICurrentUser
{
    public string Id => throw new InvalidOperationException("There is no user.");
}