using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging;

public static class MessagingModel
{
    public const string OutboxTable = "outbox_messages";

    public const string InboxTable = "inbox_messages";

    public const string DeletedUsersTable = "deleted_users";

    // The outbox, inbox and deleted-user tables of a service. ServiceDbContext calls it, so every service has them.
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable(OutboxTable);
            message.Property(m => m.Subject).HasMaxLength(OutboxMessage.SubjectMaxLength);
            message.Property(m => m.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);
            message.Property(m => m.TraceParent).HasMaxLength(OutboxMessage.TraceParentMaxLength);
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

        modelBuilder.Entity<DeletedUser>(user =>
        {
            user.ToTable(DeletedUsersTable);
            user.HasKey(u => u.Hash);
            user.Property(u => u.Hash).HasMaxLength(DeletedUser.HashLength).IsFixedLength();
            user.HasIndex(u => u.DeletedAt);
        });

        return modelBuilder;
    }
}