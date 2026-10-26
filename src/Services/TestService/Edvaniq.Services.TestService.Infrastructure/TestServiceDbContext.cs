using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.TestService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.TestService.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class TestServiceDbContext(DbContextOptions<TestServiceDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Bookmark>(bookmark =>
        {
            bookmark.Property(item => item.Url).HasMaxLength(Bookmark.UrlMaxLength);
            bookmark.Property(item => item.Title).HasMaxLength(Bookmark.TitleMaxLength);
        });
}
