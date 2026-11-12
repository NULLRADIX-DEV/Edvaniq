using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Routing;
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

    // A31 (docs/spec/acceptance.md): single choice right, multiple choice complete, multiple choice half, and true/false
    // by showing the solution. The first two count as right, the last two as wrong, each as one learning record.
    [Fact]
    public async Task AllThreeKinds_AreGradedBinary_AndShowingTheSolutionIsWrong()
    {
        var alice = User.New("alice");
        var (single, _) = await AddTopicAsync(alice);
        string[] four = ["2", "4", "5", "9"];
        var (multiple, _) = await AddTopicAsync(alice, QuestionType.MultipleChoice, four, answer: 0, answers: [0, 2]);
        var (trueFalse, _) = await AddTopicAsync(alice, QuestionType.TrueFalse, ["Wahr", "Falsch"], answer: 0);

        var singleId = await AskIdAsync(alice, single);
        var completeId = await AskIdAsync(alice, multiple);
        var halfId = await AskIdAsync(alice, multiple);
        var trueFalseId = await AskIdAsync(alice, trueFalse);
        var grades = new[]
        {
            await AnswerAsync(alice, singleId, option: 1, responseTimeMs: 0),
            await AnswerAsync(alice, completeId, options: [2, 0], responseTimeMs: 0),
            await AnswerAsync(alice, halfId, options: [0], responseTimeMs: 0),
            await RevealAsync(alice, trueFalseId, responseTimeMs: 0),
        };

        Assert.Equal([true, true, false, false], grades.Select(grade => grade.Correct));
        Assert.Equal(["singleChoice", "multipleChoice", "multipleChoice", "trueFalse"], grades.Select(grade => grade.Type));
        Assert.Equal([0, 2], grades[1].Answers);
        Assert.Equal([0, 2], grades[1].Options!);
        Assert.Equal(
            [true, true, false, false],
            new[] { singleId, completeId, halfId, trueFalseId }.Select(id => Assert.Single(Records(id)).Correct));
    }

    // The solution with its explanation comes back, nothing counts as chosen, and the time is the one measured.
    [Fact]
    public async Task ShowingTheSolution_GivesSolutionAndExplanation_WithNothingChosen()
    {
        var alice = User.New("alice");
        var (topicId, question) = await AddTopicAsync(alice);
        var attemptId = await AskIdAsync(alice, topicId);

        var revealed = await RevealAsync(alice, attemptId, responseTimeMs: 0);

        Assert.Equal(
            (true, false, (int?)null, (int[]?)null, question.Answer, question.Explanation),
            (revealed.Revealed, revealed.Correct, revealed.Option, revealed.Options, revealed.Answer, revealed.Explanation));
        Assert.True(Stored(attemptId).Revealed);
        Assert.False(Assert.Single(Records(attemptId)).Correct);
    }

    // Whichever comes first counts: an answer after the solution gets the solution back, and the other way round.
    [Fact]
    public async Task ShowingTheSolution_AndAnswering_CountOnce_WhicheverIsFirst()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var revealedFirst = await AskIdAsync(alice, topicId);
        var answeredFirst = await AskIdAsync(alice, topicId);

        var revealed = await RevealAsync(alice, revealedFirst, responseTimeMs: 0);
        var thenAnswered = await AnswerAsync(alice, revealedFirst, option: 1, responseTimeMs: 0);
        var answered = await AnswerAsync(alice, answeredFirst, option: 1, responseTimeMs: 0);
        var thenRevealed = await RevealAsync(alice, answeredFirst, responseTimeMs: 0);

        Assert.Equivalent(revealed, thenAnswered, strict: true);
        Assert.Equivalent(answered, thenRevealed, strict: true);
        Assert.False(Assert.Single(Records(revealedFirst)).Correct);
        Assert.True(Assert.Single(Records(answeredFirst)).Correct);
    }

    // Before the answer no response carries the solution: asking leaves it out (a block's quiz too, BlockQuizTests), and
    // asking and answering are the only ways to a question at all.
    [Fact]
    public async Task Solution_IsNowhere_BeforeTheAnswer()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice, QuestionType.MultipleChoice, ["2", "4", "5", "9"], answer: 0, answers: [0, 2]);

        using var asked = await AskAsync(alice, topicId);
        var attempt = await asked.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["difficulty", "id", "options", "questionId", "text", "topicId", "type"],
            attempt.EnumerateObject().Select(property => property.Name).Order());
        var routes = service.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText!.TrimEnd('/'))
            .Where(route => route is not ("/health" or "/alive" or "/me"))
            .Order();
        Assert.Equal(["/attempts", "/attempts/{id:guid}/answer", "/check-questions", "/question-pools", "/question-pools/{topicId:guid}", "/quizzes"], routes);
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

        Assert.Equivalent(first, again, strict: true);
        Assert.Equivalent(first, other, strict: true);
        Assert.Single(Records(attemptId));
    }

    [Fact]
    public async Task AnswersAtTheSameTime_CountOnce()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);
        var attemptId = await AskIdAsync(alice, topicId);

        var answers = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => AnswerAsync(alice, attemptId, option: index % 2, responseTimeMs: 0)));

        Assert.All(answers, answer => Assert.Equivalent(answers[0], answer, strict: true));
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
    [InlineData("{\"option\":1,\"showSolution\":true,\"responseTimeMs\":100}", "Option")]
    [InlineData("{\"option\":1,\"options\":[1],\"responseTimeMs\":100}", "Option")]
    [InlineData("{\"showSolution\":false,\"responseTimeMs\":100}", "Option")]
    [InlineData("{\"showSolution\":true}", "ResponseTimeMs")]
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

    // Alice's topic is known only for her, Bob gets nothing of it.
    [Fact]
    public async Task ForeignTopic_IsNotFound()
    {
        var alice = User.New("alice");
        var (topicId, _) = await AddTopicAsync(alice);

        using var response = await AskAsync(User.New("bob"), topicId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Knowledge never told this service about the topic.
    [Fact]
    public async Task UnknownTopic_IsNotFound()
    {
        using var response = await AskAsync(User.New("alice"), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    // Alice's questions of another topic don't count.
    [Fact]
    public async Task OwnTopicWithoutQuestionsYet_IsAConflict()
    {
        var alice = User.New("alice");
        var topicId = Guid.NewGuid();
        await service.AddKnownTopicAsync(alice.Id, topicId);
        await service.AddQuestionAsync(alice.Id);

        using var response = await AskAsync(alice, topicId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
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
        Guid Id,
        Guid TopicId,
        Guid QuestionId,
        string Type,
        int? Option,
        int[]? Options,
        bool Revealed,
        bool Correct,
        int Answer,
        int[] Answers,
        string Explanation,
        int ResponseTimeMs,
        DateTime AnsweredAt);

    // A topic of the user known from TopicsChanged, with one question of Content's analysis.
    private async Task<(Guid TopicId, Question Question)> AddTopicAsync(
        User user, QuestionType type = QuestionType.SingleChoice, string[]? options = null, int answer = 1, int[]? answers = null)
    {
        var topicId = Guid.NewGuid();
        await service.AddKnownTopicAsync(user.Id, topicId);
        var question = Domain.Question.Create(
            Guid.NewGuid(), user.Id, topicId, Guid.NewGuid(), 2, Guid.NewGuid(), "Was ist 2 + 2?", options ?? ["3", "4"], answer, "Zwei und zwei ist vier.", 2,
            "simulated", "questions-1", Seed.Now, type, answers);
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

    private Task<GradedDto> AnswerAsync(User user, Guid attemptId, int option, int responseTimeMs) =>
        SendAnswerAsync(user, attemptId, new { option, responseTimeMs });

    private Task<GradedDto> AnswerAsync(User user, Guid attemptId, int[] options, int responseTimeMs) =>
        SendAnswerAsync(user, attemptId, new { options, responseTimeMs });

    private Task<GradedDto> RevealAsync(User user, Guid attemptId, int responseTimeMs) =>
        SendAnswerAsync(user, attemptId, new { showSolution = true, responseTimeMs });

    private async Task<GradedDto> SendAnswerAsync(User user, Guid attemptId, object body)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync($"/attempts/{attemptId}/answer", body, TestContext.Current.CancellationToken);
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