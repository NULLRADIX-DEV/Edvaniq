using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// Every service gets the outbox and inbox tables from ServiceDbContext, shaped the way the dispatcher and the consumers
// use them. The model is built without a database.
public class MessagingModelTests
{
    [Fact]
    public void OutboxTable_HasTheNameAndColumnsTheDispatcherQueries()
    {
        using var db = TestDbContext.Create();

        var outbox = db.Model.FindEntityType(typeof(OutboxMessage))!;

        // OutboxDispatcher selects from outbox_messages by SentAt and Id.
        Assert.Equal("outbox_messages", outbox.GetTableName());
        Assert.Equal("SentAt", outbox.FindProperty(nameof(OutboxMessage.SentAt))!.GetColumnName());
        Assert.Equal("Id", outbox.FindProperty(nameof(OutboxMessage.Id))!.GetColumnName());
        Assert.Equal([nameof(OutboxMessage.Id)], outbox.FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void OutboxTable_KnowsEachMessageIdOnce_AndIndexesWhatIsSent()
    {
        using var db = TestDbContext.Create();

        var outbox = db.Model.FindEntityType(typeof(OutboxMessage))!;

        Assert.Contains(outbox.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(OutboxMessage.MessageId));
        Assert.Contains(outbox.GetIndexes(), index => index.Properties.Single().Name == nameof(OutboxMessage.SentAt));
    }

    // 00-<32 hex>-<16 hex>-<2 hex>
    [Fact]
    public void OutboxTable_HasRoomForATraceParent()
    {
        using var db = TestDbContext.Create();

        var traceParent = db.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(nameof(OutboxMessage.TraceParent))!;

        Assert.Equal(55, traceParent.GetMaxLength());
        Assert.True(traceParent.IsNullable);
    }

    [Fact]
    public void InboxTable_KnowsAMessageOncePerConsumer()
    {
        using var db = TestDbContext.Create();

        var inbox = db.Model.FindEntityType(typeof(InboxMessage))!;

        // MessagingCleanup deletes from inbox_messages by ReceivedAt.
        Assert.Equal("inbox_messages", inbox.GetTableName());
        Assert.Equal(
            [nameof(InboxMessage.MessageId), nameof(InboxMessage.Consumer)],
            inbox.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("ReceivedAt", inbox.FindProperty(nameof(InboxMessage.ReceivedAt))!.GetColumnName());
        Assert.Contains(inbox.GetIndexes(), index => index.Properties.Single().Name == nameof(InboxMessage.ReceivedAt));
    }

    [Fact]
    public void OutboxAndInbox_HaveNoOwnerFilter()
    {
        using var db = TestDbContext.Create();

        // A dispatcher or consumer runs without a user, so a filter on the owner would make it throw.
        Assert.Empty(db.Model.FindEntityType(typeof(OutboxMessage))!.GetDeclaredQueryFilters());
        Assert.Empty(db.Model.FindEntityType(typeof(InboxMessage))!.GetDeclaredQueryFilters());
    }
}