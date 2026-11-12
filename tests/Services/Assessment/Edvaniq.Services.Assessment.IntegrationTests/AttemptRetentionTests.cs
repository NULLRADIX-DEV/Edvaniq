using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// #962: attempts and block quizzes go 30 days after the question was put, younger ones stay, and asking, the block quiz
// and answering go on as before. Each test has its own users and moves the clock forward only.
public sealed class AttemptRetentionTests(RetentionFactory service) : IClassFixture<RetentionFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OldAttemptsAndQuizzes_Go_YoungerOnesStay_AndEverythingGoesOn()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);
        var topic = await AddTopicAsync(alice, questions: 4);
        var oldAttempt = await AskAsync(client, topic);
        var oldQuiz = await OpenQuizAsync(client, topic, Guid.NewGuid());

        service.Time.Advance(TimeSpan.FromDays(Attempt.RetentionDays) - TimeSpan.FromHours(1));
        var young = await AskAsync(client, topic);
        service.Time.Advance(TimeSpan.FromHours(2));

        var (quizzes, attempts) = service.Services.GetRequiredService<AttemptRetention>().RunOnce(Cancel);

        // At least these, the class shares its database and its clock.
        Assert.True(quizzes >= 1 && attempts >= 1);
        Assert.False(Query(db => db.Attempts.IgnoreQueryFilters().Any(attempt => attempt.Id == oldAttempt || attempt.QuizId == oldQuiz)));
        Assert.False(Query(db => db.BlockQuizzes.IgnoreQueryFilters().Any(quiz => quiz.Id == oldQuiz)));
        Assert.True(Query(db => db.Attempts.IgnoreQueryFilters().Any(attempt => attempt.Id == young)));

        // The young attempt is answered, its learning record goes out, and new questions and quizzes come as before.
        using var answered = await client.PostAsJsonAsync($"/attempts/{young}/answer", new { option = 1, responseTimeMs = 1200 }, Cancel);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Contains(Recorded(), recorded => recorded.AnswerId == young);
        await AskAsync(client, topic);
        await OpenQuizAsync(client, topic, Guid.NewGuid());

        // An old attempt answers like one that never was.
        using var late = await client.PostAsJsonAsync($"/attempts/{oldAttempt}/answer", new { option = 1, responseTimeMs = 1200 }, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);
    }

    // A day short of the retention, nothing goes.
    [Fact]
    public async Task YoungerAttempts_Stay()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);
        var topic = await AddTopicAsync(alice, questions: 2);
        var attempt = await AskAsync(client, topic);
        var quiz = await OpenQuizAsync(client, topic, Guid.NewGuid());

        service.Time.Advance(TimeSpan.FromDays(Attempt.RetentionDays - 1));
        service.Services.GetRequiredService<AttemptRetention>().RunOnce(Cancel);

        Assert.True(Query(db => db.Attempts.IgnoreQueryFilters().Any(item => item.Id == attempt)));
        Assert.True(Query(db => db.BlockQuizzes.IgnoreQueryFilters().Any(item => item.Id == quiz)));
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    // A known topic of the user with this many single choice questions, the right option is 1.
    private async Task<Guid> AddTopicAsync(string user, int questions)
    {
        var topicId = Guid.NewGuid();
        await service.AddKnownTopicAsync(user, topicId);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        for (var i = 0; i < questions; i++)
        {
            db.Questions.Add(Question.Create(
                Guid.NewGuid(), user, topicId, Guid.NewGuid(), 0, Guid.NewGuid(), $"Frage {i}", ["3", "4"], 1, "Zwei und zwei ist vier.", 1,
                "simulated", "questions-1", Seed.Now));
        }

        await db.SaveChangesAsync(Cancel);
        return topicId;
    }

    private static async Task<Guid> AskAsync(HttpClient client, Guid topicId)
    {
        using var response = await client.PostAsJsonAsync("/attempts", new { topicId }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OpenQuizAsync(HttpClient client, Guid topicId, Guid blockId)
    {
        using var response = await client.PostAsJsonAsync("/quizzes", new { topicId, blockId, size = 2 }, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private List<AnswerRecorded> Recorded()
    {
        var subject = EventRoute.For(typeof(AnswerRecorded)).Subject;
        return [.. Query(db => db.Set<OutboxMessage>().Where(message => message.Subject == subject).Select(message => message.Payload).ToList())
            .Select(payload => JsonSerializer.Deserialize<AnswerRecorded>(payload, JsonSerializerOptions.Web)!)];
    }

    private T Query<T>(Func<AssessmentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AssessmentDbContext>());
    }
}

// The service with a clock the tests move.
public sealed class RetentionFactory(MySqlTestServer mysql) : ServiceFactory(mysql)
{
    public TestTime Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }
}