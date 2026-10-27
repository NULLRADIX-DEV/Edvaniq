using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Domain;
using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class ContentDbContext(DbContextOptions<ContentDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.Property(item => item.Title).HasMaxLength(Document.TitleMaxLength);

        // TEXT ends at 64 KB, 200,000 characters of UTF-8 need up to 800 KB.
        document.Property(item => item.Text).HasColumnType("mediumtext");
        document.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);

        // MySQL stores no time zone, the driver reads the value back as Unspecified. Marked as UTC again, the answer
        // carries the "Z" and a browser does not take it for local time.
        document.Property(item => item.CreatedAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
