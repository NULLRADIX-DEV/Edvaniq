using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Identity.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Identity.Infrastructure;

// The service's own database. Every change to the model needs a migration
// (docs/service-internals.md#datenbank-und-migrationen).
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<Account>();
        account.Property(item => item.Name).HasMaxLength(Account.NameMaxLength);
        account.Property(item => item.PasswordHash).HasMaxLength(Account.PasswordHashMaxLength);
        account.HasIndex(item => item.Name).IsUnique();
    }
}