using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
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

    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();

    public DbSet<QuestionPool> QuestionPools => Set<QuestionPool>();

    public DbSet<AiAttempt> AiAttempts => Set<AiAttempt>();

    public DbSet<AiUsageDay> AiUsageDays => Set<AiUsageDay>();

    public DbSet<AiAnswer> AiAnswers => Set<AiAnswer>();

    public DbSet<AiSwitch> AiSwitches => Set<AiSwitch>();

    public DbSet<PageCharge> PageCharges => Set<PageCharge>();

    public DbSet<GenerationCharge> GenerationCharges => Set<GenerationCharge>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<TopicSummary> TopicSummaries => Set<TopicSummary>();

    public DbSet<TopicSummarySection> TopicSummarySections => Set<TopicSummarySection>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var document = modelBuilder.Entity<Document>();
        document.Property(item => item.Title).HasMaxLength(Document.TitleMaxLength);

        // TEXT ends at 64 KB, 200,000 characters of UTF-8 need up to 800 KB.
        document.Property(item => item.Text).HasColumnType("mediumtext");
        // The status says which job has a document. Whoever saves a new one first wins, a second worker on the same
        // document fails at its save instead of reporting it twice.
        document.Property(item => item.Status).HasConversion<string>().HasMaxLength(16).IsConcurrencyToken();
        // The topics stay in analysis while the questions follow, so the status alone doesn't tell two analyzers apart.
        document.Property(item => item.AnalyzedAt).IsConcurrencyToken();
        // Of two workers reading the same file only the first one stores its text.
        document.Property(item => item.TextExtractedAt).IsConcurrencyToken();
        document.Property(item => item.FailureReason).HasMaxLength(Document.FailureReasonMaxLength);
        document.Property(item => item.FailureCode).HasConversion<string>().HasMaxLength(24);
        document.Property(item => item.Level).HasMaxLength(Document.LevelMaxLength);
        document.Property(item => item.OutputLanguage).HasMaxLength(Document.OutputLanguageMaxLength);
        document.Property(item => item.FileFormat).HasConversion<string>().HasMaxLength(16);
        document.Property(item => item.MaterialConfirmationVersion).HasMaxLength(MaterialConfirmation.VersionMaxLength);

        // A document takes its sections along when it goes. Each place in a document exists once, so splitting the
        // same document again cannot add a section twice.
        var section = modelBuilder.Entity<Section>();
        section.HasOne<Document>().WithMany().HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Cascade);
        section.HasIndex(item => new { item.DocumentId, item.Ordinal }).IsUnique();

        // The pages of a document read from a file, each number once.
        var page = modelBuilder.Entity<DocumentPage>();
        page.HasOne<Document>().WithMany().HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Cascade);
        page.HasIndex(item => new { item.DocumentId, item.Number }).IsUnique();

        // One pool per topic. A second worker with the same topic fails at its save instead of sending the questions twice.
        var pool = modelBuilder.Entity<QuestionPool>();
        pool.HasKey(item => item.TopicId);
        pool.Property(item => item.TopicId).ValueGeneratedNever();
        pool.HasOne<Document>().WithMany().HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Cascade);

        // No foreign key to the document: what an attempt cost stays when the document goes.
        var attempt = modelBuilder.Entity<AiAttempt>();
        attempt.Property(item => item.Purpose).HasMaxLength(AiAttempt.PurposeMaxLength);
        attempt.Property(item => item.Model).HasMaxLength(AiAttempt.ModelMaxLength);
        attempt.Property(item => item.PromptVersion).HasMaxLength(AiAttempt.PromptVersionMaxLength);
        attempt.Property(item => item.Outcome).HasConversion<string>().HasMaxLength(16);
        attempt.Property(item => item.Cost).HasPrecision(18, 10);
        attempt.HasIndex(item => item.CalledAt);
        attempt.HasIndex(item => item.DocumentId);
        attempt.HasIndex(item => new { item.CallId, item.Attempt }).IsUnique();

        // One charge per document, a retry takes nothing twice. The sum of a day reads by owner and day.
        var charge = modelBuilder.Entity<PageCharge>();
        charge.HasIndex(item => item.DocumentId).IsUnique();
        charge.HasIndex(item => new { item.OwnerId, item.StudyDay });

        // One charge per topic, a retry takes nothing twice.
        var generation = modelBuilder.Entity<GenerationCharge>();
        generation.HasIndex(item => item.TopicId).IsUnique();
        generation.HasIndex(item => new { item.OwnerId, item.StudyDay });

        // The answers go with the document that asked for them. One per request and user: of two workers storing the
        // same answer the second one fails at its save and loses nothing.
        var answer = modelBuilder.Entity<AiAnswer>();
        answer.HasOne<Document>().WithMany().HasForeignKey(item => item.DocumentId).OnDelete(DeleteBehavior.Cascade);
        answer.Property(item => item.Key).HasMaxLength(AiAnswer.KeyLength).IsFixedLength();
        answer.Property(item => item.Purpose).HasMaxLength(AiAttempt.PurposeMaxLength);
        answer.Property(item => item.PromptVersion).HasMaxLength(AiAttempt.PromptVersionMaxLength);
        answer.Property(item => item.Model).HasMaxLength(AiAttempt.ModelMaxLength);
        answer.Property(item => item.Answer).HasColumnType("longtext");
        answer.HasIndex(item => new { item.OwnerId, item.Key }).IsUnique();

        var aiSwitch = modelBuilder.Entity<AiSwitch>();
        aiSwitch.Property(item => item.Id).ValueGeneratedNever();
        aiSwitch.Property(item => item.MonthlyBudget).HasPrecision(18, 6);

        var day = modelBuilder.Entity<AiUsageDay>();
        day.HasKey(item => new { item.Day, item.Purpose, item.Model, item.PromptVersion });
        day.Property(item => item.Purpose).HasMaxLength(AiAttempt.PurposeMaxLength);
        day.Property(item => item.Model).HasMaxLength(AiAttempt.ModelMaxLength);
        day.Property(item => item.PromptVersion).HasMaxLength(AiAttempt.PromptVersionMaxLength);
        day.Property(item => item.Cost).HasPrecision(18, 10);

        // The id names the bytes in the file store, so the code that wrote them sets it.
        modelBuilder.Entity<StoredFile>().Property(item => item.Id).ValueGeneratedNever();

        // #344: one summary per topic and length. Its sections go with it, and with every document they come from.
        var summary = modelBuilder.Entity<TopicSummary>();
        summary.Property(item => item.Id).ValueGeneratedNever();
        summary.Property(item => item.Length).HasConversion<string>().HasMaxLength(16);
        summary.Property(item => item.Status).HasConversion<string>().HasMaxLength(16);
        summary.Property(item => item.FailureCode).HasConversion<string>().HasMaxLength(32);
        summary.Property(item => item.Text).HasMaxLength(TopicSummaryGeneration.TextMaxLength);
        summary.Property(item => item.Model).HasMaxLength(AiAttempt.ModelMaxLength);
        summary.Property(item => item.PromptVersion).HasMaxLength(AiAttempt.PromptVersionMaxLength);
        summary.HasIndex(item => new { item.OwnerId, item.TopicId, item.Length }).IsUnique();
        summary.HasIndex(item => new { item.Status, item.NextAttemptAt });
        summary.HasMany(item => item.Sections).WithOne().HasForeignKey(item => item.SummaryId).OnDelete(DeleteBehavior.Cascade);
        summary.Navigation(item => item.Sections).UsePropertyAccessMode(PropertyAccessMode.Field);

        var summarySection = modelBuilder.Entity<TopicSummarySection>();
        summarySection.Property(item => item.Id).ValueGeneratedNever();
        summarySection.HasIndex(item => item.DocumentId);
    }
}