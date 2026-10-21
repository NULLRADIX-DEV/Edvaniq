using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging;

public static class MessagingModel
{
    public const string OutboxTable = "outbox_messages";

    public const string InboxTable = "inbox_messages";

    // The outbox and inbox tables of a service. ServiceDbContext calls it, so every service has them.
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable(OutboxTable);
            message.Property(m => m.Subject).HasMaxLength(OutboxMessage.SubjectMaxLength);
            message.Property(m => m.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);
            message.HasIndex(m => m.MessageId).IsUnique();
            message.HasIndex(m => m.SentAt);
        });

        modelBuilder.Entity<InboxMessage>(message =>
        {
            message.ToTable(InboxTable);
            message.HasKey(m => new { m.MessageId, m.Consumer });
            message.Property(m => m.Consumer).HasMaxLength(InboxMessage.ConsumerMaxLength);
            message.HasIndex(m => m.ReceivedAt);
        });

        return modelBuilder;
    }
}