using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<TopicSection> TopicSections => Set<TopicSection>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var topic = modelBuilder.Entity<Topic>();
        topic.Property(item => item.Name).HasMaxLength(Topic.NameMaxLength);
        topic.Property(item => item.AnalysisName).HasMaxLength(Topic.NameMaxLength);

        // A subtopic goes with its topic.
        topic.HasOne<Topic>().WithMany().HasForeignKey(item => item.ParentId).OnDelete(DeleteBehavior.Cascade);
        topic.HasIndex(item => new { item.OwnerId, item.LearningGoalId });

        // Content asks for the topics of a document.
        topic.HasIndex(item => new { item.OwnerId, item.DocumentId });

        var section = modelBuilder.Entity<TopicSection>();
        section.HasKey(item => new { item.TopicId, item.SectionId });

        // The default lets a rollback to a version without the column still insert rows.
        section.Property(item => item.DocumentId).HasDefaultValue(Guid.Empty);
        section.HasIndex(item => item.DocumentId);
        section.HasOne<Topic>().WithMany().HasForeignKey(item => item.TopicId).OnDelete(DeleteBehavior.Cascade);
    }
}