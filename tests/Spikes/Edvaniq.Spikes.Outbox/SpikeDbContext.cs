using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Spikes.Outbox;

public sealed class SpikeDbContext(DbContextOptions<SpikeDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    // Lands in the same SaveChanges as the change, so both commit together or not at all.
    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : notnull =>
        Outbox.Add(new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            Type = typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize(integrationEvent),
            OccurredAt = DateTime.UtcNow,
        });

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(document =>
        {
            document.Property(d => d.Title).HasMaxLength(200);
            document.HasIndex(d => d.Title).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("outbox_messages");
            message.Property(m => m.Type).HasMaxLength(200);
            message.Property(m => m.LastError).HasMaxLength(2000);
            message.HasIndex(m => m.MessageId).IsUnique();
            message.HasIndex(m => m.SentAt);
        });
    }
}

public sealed class Document
{
    public Guid Id { get; set; }

    public required string Title { get; set; }
}

public sealed record DocumentCreated(Guid DocumentId, string Title);

public sealed class OutboxMessage
{
    // Insert order, the dispatcher sends by it.
    public long Id { get; set; }

    // Goes out with the event so consumers can drop duplicates, we only guarantee at least once.
    public Guid MessageId { get; set; }

    public required string Type { get; set; }

    public required string Payload { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime? SentAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}