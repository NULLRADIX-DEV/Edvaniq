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

    public DbSet<Section> Sections => Set<Section>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.Property(item => item.Title).HasMaxLength(Document.TitleMaxLength);

        // TEXT ends at 64 KB, 200,000 characters of UTF-8 need up to 800 KB.
        document.Property(item => item.Text).HasColumnType("mediumtext");
        // The status says which job has a document. Whoever saves a new one first wins, a second worker on the same
        // document fails at its save instead of reporting it twice.
        document.Property(item => item.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
        document.Property(item => item.FailureReason).HasMaxLength(Document.FailureReasonMaxLength);

        // MySQL stores no time zone, the driver reads the value back as Unspecified. Marked as UTC again, the answer
        // carries the "Z" and a browser does not take it for local time.
        document.Property(item => item.CreatedAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // A document takes its sections along when it goes. Each place in a document exists once, so splitting the
        // same document again cannot add a section twice.
        var section = modelBuilder.Entity<Section>();
        section.HasOne<Document>().WithMany().HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Cascade);
        section.HasIndex(item => new { item.DocumentId, item.Ordinal }).IsUnique();
    }
}
