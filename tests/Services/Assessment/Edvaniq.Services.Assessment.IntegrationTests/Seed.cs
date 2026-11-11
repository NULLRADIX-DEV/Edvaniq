using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// Questions only come from events, the API has no way to add one. The tests put them into the database.
internal static class Seed
{
    public static readonly DateTime Now = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    public static Question Question(string owner, Guid? id = null) =>
        Domain.Question.Create(
            id ?? Guid.NewGuid(), owner, Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid(), "Was ist 2 + 2?", ["3", "4"], 1, "Zwei und zwei ist vier.", 1,
            "simulated", "questions-1", Now);

    public static async Task<Question> AddQuestionAsync(this ServiceFactory service, string owner, Guid? id = null)
    {
        var question = Question(owner, id);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        db.Questions.Add(question);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return question;
    }

    // A topic as TopicsChanged would have made it known.
    public static async Task AddKnownTopicAsync(this ServiceFactory service, string owner, Guid topicId)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        db.KnownTopics.Add(KnownTopic.Create(owner, topicId, Guid.Empty, hidden: false));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<Attempt> AddAttemptAsync(this ServiceFactory service, Question question)
    {
        var attempt = Attempt.Ask(question, Guid.NewGuid(), Now);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return attempt;
    }
}