using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.LearningEngine.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.LearningEngine.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class LearningEngineDbContext(DbContextOptions<LearningEngineDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<LearningRecord> LearningRecords => Set<LearningRecord>();

    public DbSet<KnownTopic> KnownTopics => Set<KnownTopic>();

    public DbSet<TopicsSnapshot> TopicsSnapshots => Set<TopicsSnapshot>();

    public DbSet<TopicIntroduction> TopicIntroductions => Set<TopicIntroduction>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<LearningRecord>();
        record.HasKey(entry => entry.AnswerId);
        record.Property(entry => entry.AnswerId).ValueGeneratedNever();
        record.HasIndex(entry => new { entry.OwnerId, entry.TopicId });

        // Topics of Knowledge, only ids (TopicsChanged).
        var topic = modelBuilder.Entity<KnownTopic>();
        topic.HasKey(item => item.TopicId);
        topic.Property(item => item.TopicId).ValueGeneratedNever();
        topic.HasIndex(item => new { item.OwnerId, item.LearningGoalId });

        var snapshot = modelBuilder.Entity<TopicsSnapshot>();
        snapshot.HasKey(item => new { item.OwnerId, item.LearningGoalId });
        snapshot.Property(item => item.ChangedAt).IsConcurrencyToken();

        // One per user and topic (SessionCompleted).
        var introduction = modelBuilder.Entity<TopicIntroduction>();
        introduction.HasKey(item => new { item.OwnerId, item.TopicId });
    }
}