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

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<LearningRecord>();
        record.HasKey(entry => entry.AnswerId);
        record.Property(entry => entry.AnswerId).ValueGeneratedNever();

        // MySQL keeps no kind, the column only ever holds UTC.
        record.Property(entry => entry.AnsweredAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}