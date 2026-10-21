using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Spikes.Broker.Consumer;

public sealed class InboxDbContext(DbContextOptions<InboxDbContext> options) : DbContext(options)
{
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(message =>
        {
            message.ToTable("inbox_messages");
            message.HasKey(m => m.MessageId);
        });

        modelBuilder.Entity<Notification>().Property(n => n.Text).HasMaxLength(300);
    }
}

public sealed record DocumentCreated(Guid DocumentId, string Title);

public sealed class InboxMessage
{
    // Primary key, so even two consumers can't store the same message twice.
    public Guid MessageId { get; set; }

    public DateTime ReceivedAt { get; set; }
}

public sealed class Notification
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public required string Text { get; set; }
}