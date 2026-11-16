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

    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

    public DbSet<OutgoingMail> OutgoingMails => Set<OutgoingMail>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<Account>();
        account.Property(item => item.Name).HasMaxLength(Account.NameMaxLength);
        account.Property(item => item.PasswordHash).HasMaxLength(Account.PasswordHashMaxLength);
        account.HasIndex(item => item.Name).IsUnique();

        // #244: user accounts. Every new column takes a default or null, so the previous release still writes its team
        // accounts after the migration.
        account.Property(item => item.Kind).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AccountKind.Team);
        account.Property(item => item.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AccountStatus.Active);
        account.Property(item => item.Email).HasMaxLength(AccountEmail.MaxLength);
        account.Property(item => item.Pseudonym).HasMaxLength(Account.PseudonymMaxLength);
        account.HasIndex(item => item.Email).IsUnique();

        // #245: invitation codes, only their hash. The status is computed from the two times.
        var invite = modelBuilder.Entity<InviteCode>();
        invite.Property(item => item.Label).HasMaxLength(InviteCode.LabelLength);
        invite.Property(item => item.CodeHash).HasMaxLength(64);
        invite.Ignore(item => item.Status);
        invite.HasIndex(item => item.Label).IsUnique();
        invite.HasIndex(item => item.CodeHash).IsUnique();

        // #248: system mails on their way, gone with their account.
        var mail = modelBuilder.Entity<OutgoingMail>();
        mail.Property(item => item.Kind).HasConversion<string>().HasMaxLength(32);
        mail.Property(item => item.To).HasMaxLength(AccountEmail.MaxLength);
        mail.Property(item => item.Subject).HasMaxLength(OutgoingMail.SubjectMaxLength);
        mail.Property(item => item.Body).HasMaxLength(OutgoingMail.BodyMaxLength);
        mail.Ignore(item => item.ExpiresAt);
        mail.HasOne<Account>().WithMany().HasForeignKey(item => item.AccountId).OnDelete(DeleteBehavior.Cascade);
        mail.HasIndex(item => item.DueAt);
        mail.HasIndex(item => item.CreatedAt);
    }
}