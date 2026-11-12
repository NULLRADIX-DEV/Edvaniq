using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Knowledge.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Knowledge.Infrastructure;

// The service's own database. ServiceDbContext limits every entity that belongs to a user to the current user. Every
// change to the model needs a migration (docs/service-internals.md#datenbank-und-migrationen).
public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<TopicSection> TopicSections => Set<TopicSection>();

    public DbSet<DeletedDocument> DeletedDocuments => Set<DeletedDocument>();

    public DbSet<TopicSet> TopicSets => Set<TopicSet>();

    public DbSet<KeyTerm> KeyTerms => Set<KeyTerm>();

    // A save that adds or changes topics must change their TopicSet too (ITopicState.TouchAsync), else two saves on one
    // learning goal could pass each other. Deleting brings no name twice and is left out, so a user with topics from before
    // TopicSets still goes as a whole. A save that loses on a TopicSet found the topics changed meanwhile: no failure, it
    // tries again on the new state.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CheckTopicSets();
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException exception) when (IsTopicSetConflict(exception))
        {
            throw new MessageNotReadyException("The topics of the learning goal changed meanwhile.");
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CheckTopicSets();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsTopicSetConflict(exception))
        {
            throw new MessageNotReadyException("The topics of the learning goal changed meanwhile.");
        }
    }

    private void CheckTopicSets()
    {
        var touched = ChangeTracker.Entries<TopicSet>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => (entry.Entity.OwnerId, entry.Entity.LearningGoalId))
            .ToHashSet();
        foreach (var entry in ChangeTracker.Entries<Topic>())
        {
            Guid[] goals = entry.State switch
            {
                EntityState.Added => [entry.Entity.LearningGoalId],
                EntityState.Modified => [entry.Entity.LearningGoalId, entry.Property(topic => topic.LearningGoalId).OriginalValue],
                _ => [],
            };
            var missing = goals.Where(goal => !touched.Contains((entry.Entity.OwnerId, goal))).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Topic {entry.Entity.Id} changes without the TopicSet of learning goal {missing[0]}, call ITopicState.TouchAsync.");
            }
        }
    }

    // Lost on the version, or two first changes of one goal at once and the second insert hit the key.
    private bool IsTopicSetConflict(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
            ? exception.Entries.Any(entry => entry.Entity is TopicSet)
            : ChangeTracker.Entries<TopicSet>().Any(entry => entry.State == EntityState.Added)
                && exception.InnerException?.Message.Contains("TopicSets.PRIMARY", StringComparison.Ordinal) == true;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var topic = modelBuilder.Entity<Topic>();
        topic.Property(item => item.Name).HasMaxLength(Topic.NameMaxLength);
        topic.Property(item => item.AnalysisName).HasMaxLength(Topic.NameMaxLength);

        // A subtopic goes with its topic.
        topic.HasOne<Topic>().WithMany().HasForeignKey(item => item.ParentId).OnDelete(DeleteBehavior.Cascade);
        topic.HasIndex(item => new { item.OwnerId, item.LearningGoalId });

        // Content asks for the topics of a document.
        topic.HasIndex(item => new { item.OwnerId, item.DocumentId });

        var section = modelBuilder.Entity<TopicSection>();
        section.HasKey(item => new { item.TopicId, item.SectionId });

        // The default lets a rollback to a version without the column still insert rows.
        section.Property(item => item.DocumentId).HasDefaultValue(Guid.Empty);
        section.HasIndex(item => item.DocumentId);
        section.HasOne<Topic>().WithMany().HasForeignKey(item => item.TopicId).OnDelete(DeleteBehavior.Cascade);

        // A term goes with its topic. Content asks nothing of it, the user reads them per topic.
        var term = modelBuilder.Entity<KeyTerm>();
        term.Property(item => item.Term).HasMaxLength(KeyTerm.TermMaxLength);
        term.Property(item => item.Definition).HasMaxLength(KeyTerm.DefinitionMaxLength);
        term.HasOne<Topic>().WithMany().HasForeignKey(item => item.TopicId).OnDelete(DeleteBehavior.Cascade);
        term.HasIndex(item => new { item.OwnerId, item.DocumentId });

        var set = modelBuilder.Entity<TopicSet>();
        set.HasKey(item => new { item.OwnerId, item.LearningGoalId });
        set.Property(item => item.LearningGoalId).ValueGeneratedNever();
        set.Property(item => item.Version).IsConcurrencyToken();

        var deleted = modelBuilder.Entity<DeletedDocument>();
        deleted.HasKey(item => new { item.OwnerId, item.DocumentId });
        deleted.Property(item => item.DocumentId).ValueGeneratedNever();
    }
}