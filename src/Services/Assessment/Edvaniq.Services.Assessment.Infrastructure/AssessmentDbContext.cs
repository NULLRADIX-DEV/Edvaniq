using System.Text.Json;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Assessment.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Edvaniq.Services.Assessment.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class AssessmentDbContext(DbContextOptions<AssessmentDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<Question> Questions => Set<Question>();

    public DbSet<Attempt> Attempts => Set<Attempt>();

    public DbSet<KnownTopic> KnownTopics => Set<KnownTopic>();

    public DbSet<TopicsSnapshot> TopicsSnapshots => Set<TopicsSnapshot>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var question = modelBuilder.Entity<Question>();
        question.Property(entry => entry.Id).ValueGeneratedNever();
        question.Property(entry => entry.Text).HasMaxLength(Question.TextMaxLength);
        question.Property(entry => entry.Explanation).HasMaxLength(Question.ExplanationMaxLength);
        question.Property(entry => entry.ModelId).HasMaxLength(Question.ModelIdMaxLength);
        question.Property(entry => entry.PromptVersion).HasMaxLength(Question.PromptVersionMaxLength);

        // A json column lives outside the row, so the options never count against MySQL's row size.
        question.Property(entry => entry.Options)
            .HasColumnType("json")
            .HasConversion(
                options => JsonSerializer.Serialize(options, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<List<string>>(json, JsonSerializerOptions.Web) ?? new List<string>(),
                new ValueComparer<IReadOnlyList<string>>(
                    (left, right) => left!.SequenceEqual(right!),
                    options => options.Aggregate(0, (hash, option) => HashCode.Combine(hash, option.GetHashCode(StringComparison.Ordinal))),
                    options => options.ToList()));

        question.HasIndex(entry => new { entry.OwnerId, entry.DocumentId, entry.TopicPosition });
        question.HasIndex(entry => new { entry.OwnerId, entry.TopicId });

        var attempt = modelBuilder.Entity<Attempt>();
        attempt.Property(entry => entry.Id).ValueGeneratedNever();

        // Two answers at once: the later one updates no row and gets the stored answer instead.
        attempt.Property(entry => entry.Answered).IsConcurrencyToken();
        attempt.HasIndex(entry => entry.OwnerId);

        // Topics of Knowledge, only ids (TopicsChanged).
        var topic = modelBuilder.Entity<KnownTopic>();
        topic.HasKey(item => item.TopicId);
        topic.Property(item => item.TopicId).ValueGeneratedNever();
        topic.HasIndex(item => new { item.OwnerId, item.LearningGoalId });

        var snapshot = modelBuilder.Entity<TopicsSnapshot>();
        snapshot.HasKey(item => new { item.OwnerId, item.LearningGoalId });
        snapshot.Property(item => item.ChangedAt).IsConcurrencyToken();
    }
}