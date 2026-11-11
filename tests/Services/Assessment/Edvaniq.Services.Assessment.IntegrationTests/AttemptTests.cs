using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// The user gets a question of a topic, answers it and sees the solution right away. The answer is graded right or
// wrong, stored once however often it comes in, with its response time, and reported as a learning record
// (docs/spec/events.md#lern-nachweis).
public sealed class AttemptTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly string Subject = EventRoute.For(typeof(AnswerRecorded)).Subject;

    [Fact]
    public async Task Question_ComesWithoutItsSolution_TheAnswerWithIt()
    {
        var alice = User.New("alice");
        var (topicId, question) = await AddTopicAsync(alice);

        using var asked = await AskAsync(alice, topicId);
        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        var attempt = await asked.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((topicId, question.Id, question.Text), (attempt.GetProperty("topicId").GetGuid(), attempt.GetProperty("questionId").GetGuid(), attempt.GetProperty("text").GetString()));
        Assert.Equal(question.Options, attempt.GetProperty("options").Deserialize<string[]>());
        Assert.False(attempt.TryGetProperty("answer", out _));
        Assert.False(attempt.TryGetProperty("explanation", out _));

        var graded = await AnswerAsync(alice, attempt.GetProperty("id").GetGuid(), option: 1, responseTimeMs: 0);

        Assert.Equal((true, 1, question.Explanation), (graded.Correct, graded.Answer, graded.Explanation));
    }

    [Fact]
    public async Task WrongOption_IsGradedWrong_AndShowsTheRightOne()
    {
        var alice = User.New("alice");
        var (topicId, question) = await AddTopicAsync(alice);

        var graded = await AnswerAsync(alice, await AskIdAsync(alice, topicId), option: 0, responseTimeMs: 0);

        Assert.Equal((false, 0, question.Answer, question.Explanation), (graded.Correct, graded.Option, graded.Answer, graded.Explanation));
    }

    // A double click, a retry and a second answer to the same question all get the first answer back.
    [Fact]
    public async Task SameAnswerSentTwice_CountsOnce()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var attemptId = await AskIdAsync(alice, topicId);

        var first = await AnswerAsync(alice, attemptId, option: 1, responseTimeMs: 0);
        var again = await AnswerAsync(alice, attemptId, option: 1, responseTimeMs: 0);
        var other = await AnswerAsync(alice, attemptId, option: 0, responseTimeMs: 0);

        Assert.Equal(first, again);
        Assert.Equal(first, other);
        Assert.Single(Records(attemptId));
    }

    [Fact]
    public async Task AnswersAtTheSameTime_CountOnce()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var attemptId = await AskIdAsync(alice, topicId);

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => AnswerAsync(alice, attemptId, option: index % 2, responseTimeMs: 0)));

        Assert.All(answers, answer => Assert.Equal(answers[0], answer));
        var record = Assert.Single(Records(attemptId));
        Assert.Equal(answers[0].Correct, record.Correct);
    }

    [Fact]
    public async Task Answer_IsReportedAsALearningRecord()
    {
        var alice = User.New("alice");
        var (topicId, question) = await AddTopicAsync(alice);
        var attemptId = await AskIdAsync(alice, topicId);

        var graded = await AnswerAsync(alice, attemptId, option: 1, responseTimeMs: 0);

        var record = Assert.Single(Records(attemptId));
        Assert.Equal(
            new AnswerRecorded(
                AnswerRecorded.CurrentVersion, alice.Id, topicId, question.Id, null, attemptId, true, question.Difficulty, 0, null,
                Mode.Practice, graded.AnsweredAt),
            record with { AnsweredAt = record.AnsweredAt.ToUniversalTime() });
        Assert.InRange(record.AnsweredAt.ToUniversalTime(), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    // The client measures, the server keeps it within the time the question was out.
    [Fact]
    public async Task ResponseTime_IsStored_AtMostTheTimeSinceAsking()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var shortId = await AskIdAsync(alice, topicId);
        var longId = await AskIdAsync(alice, topicId);

        var fast = await AnswerAsync(alice, shortId, option: 1, responseTimeMs: 0);
        var hour = await AnswerAsync(alice, longId, option: 1, responseTimeMs: 3_600_000);

        Assert.Equal(0, fast.ResponseTimeMs);
        Assert.InRange(hour.ResponseTimeMs, 0, 60_000);
        Assert.Equal(hour.ResponseTimeMs, Stored(longId).ResponseTimeMs);
        Assert.Equal(hour.ResponseTimeMs, Assert.Single(Records(longId)).ResponseTimeMs);
    }

    [Theory]
    [InlineData("{\"option\":1}", "ResponseTimeMs")]
    [InlineData("{\"option\":1,\"responseTimeMs\":-1}", "ResponseTimeMs")]
    [InlineData("{\"responseTimeMs\":100}", "Option")]
    [InlineData("{\"option\":7,\"responseTimeMs\":100}", "Option")]
    public async Task Answer_WithoutAnOptionOfTheQuestionOrAValidTime_IsRejected(string body, string field)
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var attemptId = await AskIdAsync(alice, topicId);
        using var client = ClientFor(alice);

        using var response = await client.PostAsync(
            $"/attempts/{attemptId}/answer", new StringContent(body, MediaTypeHeaderValue.Parse("application/json")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        Assert.False(Stored(attemptId).Answered);
        Assert.Empty(Records(attemptId));
    }

    // Knowledge only knows the topic under Alice's token, so Bob's own token gets him nothing.
    [Fact]
    public async Task ForeignTopic_IsNotFound()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var bob = User.New("bob");

        using var response = await AskAsync(bob, topicId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains($"Bearer {bob.Token}", service.Knowledge.Authorizations);
    }

    [Fact]
    public async Task ForeignAttempt_IsNotFound()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var attemptId = await AskIdAsync(alice, topicId);
        using var client = ClientFor(User.New("bob"));

        using var response = await client.PostAsJsonAsync(
            $"/attempts/{attemptId}/answer", new { option = 1, responseTimeMs = 100 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(Stored(attemptId).Answered);
    }

    // The questions arrive some time after the topics, the client can tell that apart from a topic that isn't there.
    [Fact]
    public async Task OwnTopicWithoutQuestionsYet_IsAConflict()
    {
        var alice = User.New("alice");
        var topicId = Guid.NewGuid();
        service.Knowledge.Add(alice.Token, topicId, Guid.NewGuid(), 0);

        using var response = await AskAsync(alice, topicId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task KnowledgeDown_IsAProblemWithTheTrace_WithoutDetails()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        service.Knowledge.Down = true;
        try
        {
            using var response = await AskAsync(alice, topicId);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal(TraceId, JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString());
            Assert.DoesNotContain("knowledge", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
            Assert.Empty(service.Logs.Containing(alice.Token));
        }
        finally
        {
            service.Knowledge.Down = false;
        }
    }

    private sealed record User(string Id, string Token)
    {
        public static User New(string name)
        {
            var id = $"{name}-{Guid.NewGuid():N}";
            return new(id, TestTokens.Create(id));
        }
    }

    private sealed record GradedDto(
        Guid Id, Guid TopicId, Guid QuestionId, int Option, bool Correct, int Answer, string Explanation, int ResponseTimeMs, DateTime AnsweredAt);

    // A topic of Knowledge with one question of Content's analysis.
    private async Task<(Guid TopicId, Question Question)> AddTopicAsync(User user)
    {
        var documentId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        service.Knowledge.Add(user.Token, topicId, documentId, 2);
        var question = Domain.Question.Create(
            Guid.NewGuid(), user.Id, documentId, 2, Guid.NewGuid(), "Was ist 2 + 2?", ["3", "4"], 1, "Zwei und zwei ist vier.", 2,
            "simulated", "questions-1", Seed.Now);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        db.Questions.Add(question);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (topicId, question);
    }

    private HttpClient ClientFor(User user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        client.DefaultRequestHeaders.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");
        return client;
    }

    private async Task<HttpResponseMessage> AskAsync(User user, Guid topicId)
    {
        using var client = ClientFor(user);
        return await client.PostAsJsonAsync("/attempts", new { topicId }, TestContext.Current.CancellationToken);
    }

    private async Task<Guid> AskIdAsync(User user, Guid topicId)
    {
        using var response = await AskAsync(user, topicId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private async Task<GradedDto> AnswerAsync(User user, Guid attemptId, int option, int responseTimeMs)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync(
            $"/attempts/{attemptId}/answer", new { option, responseTimeMs }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GradedDto>(TestContext.Current.CancellationToken))!;
    }

    private Attempt Stored(Guid attemptId) =>
        Query(db => db.Attempts.IgnoreQueryFilters().AsNoTracking().Single(attempt => attempt.Id == attemptId));

    // The learning records of one answer in the outbox, sent or not.
    private List<AnswerRecorded> Records(Guid answerId) =>
        Query(db => db.Set<OutboxMessage>().AsNoTracking().Where(message => message.Subject == Subject).Select(message => message.Payload).ToList())
            .Select(payload => JsonSerializer.Deserialize<AnswerRecorded>(payload, JsonSerializerOptions.Web)!)
            .Where(record => record.AnswerId == answerId)
            .ToList();

    private T Query<T>(Func<AssessmentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AssessmentDbContext>());
    }
}