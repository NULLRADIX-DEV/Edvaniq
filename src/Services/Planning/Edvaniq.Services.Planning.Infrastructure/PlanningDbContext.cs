using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class PlanningDbContext(DbContextOptions<PlanningDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<ExampleItem> ExampleItems => Set<ExampleItem>();

    public DbSet<PingRecord> Pings => Set<PingRecord>();

    public DbSet<KnownTopic> KnownTopics => Set<KnownTopic>();

    public DbSet<TopicsSnapshot> TopicsSnapshots => Set<TopicsSnapshot>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExampleItem>().Property(item => item.Text).HasMaxLength(ExampleItem.TextMaxLength);

        // Topics of Knowledge, only ids (TopicsChanged).
        var topic = modelBuilder.Entity<KnownTopic>();
        topic.HasKey(item => item.TopicId);
        topic.Property(item => item.TopicId).ValueGeneratedNever();
        topic.HasIndex(item => new { item.OwnerId, item.LearningGoalId });

        var snapshot = modelBuilder.Entity<TopicsSnapshot>();
        snapshot.HasKey(item => new { item.OwnerId, item.LearningGoalId });
        snapshot.Property(item => item.ChangedAt).IsConcurrencyToken();
    }
}