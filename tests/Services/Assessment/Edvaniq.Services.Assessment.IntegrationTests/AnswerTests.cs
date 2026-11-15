using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// #403: GET /answers names the answers of a block's quiz and of the sessions' check questions, for Planning's session
// result. Only answered ones, only the caller's.
public sealed class AnswerTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task BlockQuiz_GivesItsAnsweredQuestions_ShownSolutionAsWrong()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 5);
        var block = Guid.NewGuid();
        var quiz = await OpenAsync(alice, topic, block, size: 4);

        await AnswerAsync(alice, quiz[0], new { option = 1, responseTimeMs = 0 });
        await AnswerAsync(alice, quiz[1], new { option = 0, responseTimeMs = 0 });
        await AnswerAsync(alice, quiz[2], new { showSolution = true, responseTimeMs = 0 });

        var (status, answers) = await AnswersAsync(alice, $"blockId={block}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(quiz.Take(3).Order(), answers.Select(answer => answer.AnswerId).Order());
        Assert.Equal([true, false, false], quiz.Take(3).Select(id => answers.Single(answer => answer.AnswerId == id).Correct));
        Assert.All(answers, answer => Assert.Equal(topic, answer.TopicId));
    }

    // Only the named fields: nothing of the question or the chosen option.
    [Fact]
    public async Task Answer_NamesOnlyItsFields()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 3);
        var block = Guid.NewGuid();
        var quiz = await OpenAsync(alice, topic, block, size: 1);
        await AnswerAsync(alice, quiz[0], new { option = 1, responseTimeMs = 0 });

        using var client = ClientFor(alice);
        var body = await client.GetFromJsonAsync<JsonElement>($"/answers?blockId={block}", TestContext.Current.CancellationToken);

        Assert.Equal(["answers"], body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["answerId", "answeredAt", "correct", "topicId"],
            body.GetProperty("answers")[0].EnumerateObject().Select(property => property.Name).Order());
    }

    // A block broken off and started again has several sessions: the block's quiz and the check questions of each
    // session named come together, those of another session don't.
    [Fact]
    public async Task BlockAndSessions_GiveQuizAndCheckQuestions_OfThoseNamedOnly()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 5);
        var block = Guid.NewGuid();
        var (first, other) = (Guid.NewGuid(), Guid.NewGuid());
        var checks = await CheckAsync(alice, topic, first);
        var otherChecks = await CheckAsync(alice, topic, other);
        var quiz = await OpenAsync(alice, topic, block, size: 2);
        foreach (var id in checks.Concat(otherChecks).Append(quiz[0]))
        {
            await AnswerAsync(alice, id, new { option = 1, responseTimeMs = 0 });
        }

        var (_, both) = await AnswersAsync(alice, $"blockId={block}&sessionId={first}");
        var (_, sessionOnly) = await AnswersAsync(alice, $"sessionId={first}");

        Assert.Equal(checks.Append(quiz[0]).Order(), both.Select(answer => answer.AnswerId).Order());
        Assert.Equal(checks.Order(), sessionOnly.Select(answer => answer.AnswerId).Order());
    }

    [Fact]
    public async Task OpenQuestions_AreNotAnswers()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 3);
        var block = Guid.NewGuid();
        await OpenAsync(alice, topic, block, size: 3);

        var (status, answers) = await AnswersAsync(alice, $"blockId={block}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(answers);
    }

    // Someone else's block or session looks like one without answers.
    [Fact]
    public async Task ForeignBlockAndSession_GiveNoAnswers()
    {
        var (alice, bob) = (User.New("alice"), User.New("bob"));
        var topic = await AddTopicAsync(alice, questions: 5);
        var (block, session) = (Guid.NewGuid(), Guid.NewGuid());
        var checks = await CheckAsync(alice, topic, session);
        var quiz = await OpenAsync(alice, topic, block, size: 2);
        foreach (var id in checks.Append(quiz[0]))
        {
            await AnswerAsync(alice, id, new { option = 1, responseTimeMs = 0 });
        }

        var (status, answers) = await AnswersAsync(bob, $"blockId={block}&sessionId={session}");
        var (unknownStatus, unknown) = await AnswersAsync(alice, $"blockId={Guid.NewGuid()}");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (status, unknownStatus));
        Assert.Empty(answers);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task NeitherBlockNorSession_IsABadRequest()
    {
        var (status, _) = await AnswersAsync(User.New("alice"), "");

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task SessionLimit_HoldsTwentyAndRejectsMore()
    {
        var alice = User.New("alice");
        static string Sessions(int count) => string.Join('&', Enumerable.Range(0, count).Select(_ => $"sessionId={Guid.NewGuid()}"));

        var (atLimit, _) = await AnswersAsync(alice, Sessions(20));
        var (over, _) = await AnswersAsync(alice, Sessions(21));

        Assert.Equal(HttpStatusCode.OK, atLimit);
        Assert.Equal(HttpStatusCode.BadRequest, over);
    }

    private sealed record User(string Id, string Token)
    {
        public static User New(string name)
        {
            var id = $"{name}-{Guid.NewGuid():N}";
            return new(id, TestTokens.Create(id));
        }
    }

    private sealed record AnswerDto(Guid AnswerId, Guid TopicId, bool Correct, DateTime AnsweredAt);

    private sealed record AnswerListDto(List<AnswerDto> Answers);

    private sealed record QuizDto(List<QuestionDto> Questions);

    private sealed record QuestionDto(Guid Id);

    // A known topic of the user with this many single choice questions, the right option is 1.
    private async Task<Guid> AddTopicAsync(User user, int questions)
    {
        var topicId = Guid.NewGuid();
        await service.AddKnownTopicAsync(user.Id, topicId);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        for (var i = 0; i < questions; i++)
        {
            db.Questions.Add(Question.Create(
                Guid.NewGuid(), user.Id, topicId, Guid.NewGuid(), 0, Guid.NewGuid(), $"Frage {i}", ["3", "4"], 1, "Zwei und zwei ist vier.", 1,
                "simulated", "questions-1", Seed.Now));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return topicId;
    }

    private HttpClient ClientFor(User user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client;
    }

    private async Task<List<Guid>> OpenAsync(User user, Guid topicId, Guid blockId, int size)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync("/quizzes", new { topicId, blockId, size }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quiz = await response.Content.ReadFromJsonAsync<QuizDto>(TestContext.Current.CancellationToken);
        return [.. quiz!.Questions.Select(question => question.Id)];
    }

    private async Task<List<Guid>> CheckAsync(User user, Guid topicId, Guid sessionId)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync("/check-questions", new { topicId, sessionId }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<QuizDto>(TestContext.Current.CancellationToken);
        Assert.NotEmpty(check!.Questions);
        return [.. check.Questions.Select(question => question.Id)];
    }

    private async Task AnswerAsync(User user, Guid attemptId, object answer)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync($"/attempts/{attemptId}/answer", answer, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(HttpStatusCode Status, List<AnswerDto> Answers)> AnswersAsync(User user, string query)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync($"/answers?{query}", TestContext.Current.CancellationToken);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, (await response.Content.ReadFromJsonAsync<AnswerListDto>(TestContext.Current.CancellationToken))!.Answers)
            : (response.StatusCode, []);
    }
}