using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Planning.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Edvaniq.Services.Planning.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class PlanningDbContext(DbContextOptions<PlanningDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<ExampleItem> ExampleItems => Set<ExampleItem>();

    public DbSet<PingRecord> Pings => Set<PingRecord>();

    public DbSet<KnownTopic> KnownTopics => Set<KnownTopic>();

    public DbSet<TopicsSnapshot> TopicsSnapshots => Set<TopicsSnapshot>();

    public DbSet<LearningGoal> LearningGoals => Set<LearningGoal>();

    public DbSet<DailyBudgetSetting> DailyBudgets => Set<DailyBudgetSetting>();

    public DbSet<DoneBlock> DoneBlocks => Set<DoneBlock>();

    public DbSet<Session> Sessions => Set<Session>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExampleItem>().Property(item => item.Text).HasMaxLength(ExampleItem.TextMaxLength);

        // The id of the default goal comes from the user id, the key keeps it to one per user.
        var goal = modelBuilder.Entity<LearningGoal>();
        goal.Property(item => item.Id).ValueGeneratedNever();

        // The choices as their names, readable in the database and safe to extend.
        goal.Property(item => item.Kind).HasConversion<string>().HasMaxLength(32);
        goal.Property(item => item.Name).HasMaxLength(LearningGoalFields.NameMaxLength);
        goal.Property(item => item.ExamType).HasConversion<string>().HasMaxLength(32);

        // Oracle's provider writes a DateOnly but reads a date column back as DateTime.
        goal.Property(item => item.ExamDate)
            .HasConversion(new ValueConverter<DateOnly, DateTime>(
                date => date.ToDateTime(TimeOnly.MinValue),
                stored => DateOnly.FromDateTime(stored)))
            .HasColumnType("date");
        goal.Property(item => item.Level).HasConversion<string>().HasMaxLength(32);
        goal.Property(item => item.OutputLanguage).HasMaxLength(8);
        goal.Property(item => item.Goal).HasMaxLength(LearningGoalFields.GoalMaxLength);

        // Topics of Knowledge, only ids (TopicsChanged).
        var topic = modelBuilder.Entity<KnownTopic>();
        topic.HasKey(item => item.TopicId);
        topic.Property(item => item.TopicId).ValueGeneratedNever();
        topic.HasIndex(item => new { item.OwnerId, item.LearningGoalId });

        var snapshot = modelBuilder.Entity<TopicsSnapshot>();
        snapshot.HasKey(item => new { item.OwnerId, item.LearningGoalId });
        snapshot.Property(item => item.ChangedAt).IsConcurrencyToken();

        // One stored budget per user.
        modelBuilder.Entity<DailyBudgetSetting>().HasKey(item => item.OwnerId);

        // Done blocks (#375). Their id comes from the plan. Deleting the goal deletes its blocks.
        var done = modelBuilder.Entity<DoneBlock>();
        done.Property(item => item.Id).ValueGeneratedNever();
        done.Property(item => item.Kind).HasConversion<string>().HasMaxLength(16);
        done.Property(item => item.StudyDay)
            .HasConversion(new ValueConverter<DateOnly, DateTime>(
                date => date.ToDateTime(TimeOnly.MinValue),
                stored => DateOnly.FromDateTime(stored)))
            .HasColumnType("date");
        done.HasIndex(item => new { item.OwnerId, item.LearningGoalId, item.StudyDay });
        done.HasOne<LearningGoal>().WithMany().HasForeignKey(item => item.LearningGoalId).OnDelete(DeleteBehavior.Cascade);

        // Sessions (#376). MySQL allows any number of NULLs in a unique index, so ended sessions don't count and of the
        // running ones there is one per user.
        var session = modelBuilder.Entity<Session>();
        session.Property(item => item.Id).ValueGeneratedNever();
        session.Property(item => item.ActiveOwnerId).HasMaxLength(128).HasAnnotation(CollationAnnotation, OwnerIdCollation);
        session.HasIndex(item => item.ActiveOwnerId).IsUnique();
        session.Property(item => item.Kind).HasConversion<string>().HasMaxLength(16);
        session.Ignore(item => item.Steps);
        session.Ignore(item => item.ActiveSeconds);

        // Of two signals at once only one counts the gap (#394), the other one goes again on the stored session.
        session.Property(item => item.LastActivityAt).IsConcurrencyToken();
        session.Property(item => item.Outcome).HasConversion<string>().HasMaxLength(16);
        session.Property(item => item.StudyDay)
            .HasConversion(new ValueConverter<DateOnly, DateTime>(
                date => date.ToDateTime(TimeOnly.MinValue),
                stored => DateOnly.FromDateTime(stored)))
            .HasColumnType("date");
        session.HasOne<LearningGoal>().WithMany().HasForeignKey(item => item.LearningGoalId).OnDelete(DeleteBehavior.Cascade);
    }
}