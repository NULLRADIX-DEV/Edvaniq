using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Domain;
using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Planning.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-template.md#datenbank-und-migrationen).
public sealed class PlanningDbContext(DbContextOptions<PlanningDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<ExampleItem> ExampleItems => Set<ExampleItem>();

    public DbSet<PingRecord> Pings => Set<PingRecord>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExampleItem>().Property(item => item.Text).HasMaxLength(ExampleItem.TextMaxLength);

        // MySQL stores no time zone, the driver reads the value back as Unspecified. Marked as UTC again, the answer
        // carries the "Z" and a browser does not take it for local time.
        modelBuilder.Entity<PingRecord>().Property(ping => ping.PingedAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
