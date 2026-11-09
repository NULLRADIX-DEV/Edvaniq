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

        // MySQL keeps no kind, the column only ever holds UTC.
        question.Property(entry => entry.CreatedAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        question.HasIndex(entry => new { entry.OwnerId, entry.DocumentId, entry.TopicPosition });
    }
}