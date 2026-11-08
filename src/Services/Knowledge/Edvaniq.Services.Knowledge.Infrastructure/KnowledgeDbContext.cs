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

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var topic = modelBuilder.Entity<Topic>();
        topic.Property(item => item.Name).HasMaxLength(Topic.NameMaxLength);

        // MySQL stores no time zone, the driver reads the value back as Unspecified. Marked as UTC again, the answer
        // carries the "Z" and a browser does not take it for local time.
        topic.Property(item => item.CreatedAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // A subtopic goes with its topic.
        topic.HasOne<Topic>().WithMany().HasForeignKey(item => item.ParentId).OnDelete(DeleteBehavior.Cascade);
        topic.HasIndex(item => new { item.OwnerId, item.LearningGoalId });
    }
}