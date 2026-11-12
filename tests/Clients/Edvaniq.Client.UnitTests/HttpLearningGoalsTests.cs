using System.Net;
using System.Text;
using System.Text.Json;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// #273: learning goals through the web host: the path, what is sent, and how Planning's answers come back.
public sealed class HttpLearningGoalsTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Id = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";

    private static readonly NewLearningGoal Bio = new("ExamPreparation", "Bio", "Klausur", "2027-03-01", "UpperSecondary", "de", "eine 2");

    [Fact]
    public async Task Create_SendsAllFields_AndReadsTheGoal()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();
        var goals = Goals(requests, Answer(HttpStatusCode.Created, $$"""
            {"id":"{{Id}}","kind":"ExamPreparation","name":"Bio","examType":"Klausur","examDate":"2027-03-01",
             "level":"UpperSecondary","outputLanguage":"de","goal":"eine 2","createdAt":"2026-11-12T10:00:00Z"}
            """));

        var outcome = await goals.CreateAsync(Bio, TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/learning-goals"), request.RequestUri);
        using var sent = JsonDocument.Parse(body);
        Assert.Equal("ExamPreparation", sent.RootElement.GetProperty("kind").GetString());
        Assert.Equal("Bio", sent.RootElement.GetProperty("name").GetString());
        Assert.Equal("Klausur", sent.RootElement.GetProperty("examType").GetString());
        Assert.Equal("2027-03-01", sent.RootElement.GetProperty("examDate").GetString());
        Assert.Equal("UpperSecondary", sent.RootElement.GetProperty("level").GetString());
        Assert.Equal("de", sent.RootElement.GetProperty("outputLanguage").GetString());
        Assert.Equal("eine 2", sent.RootElement.GetProperty("goal").GetString());
        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal(Guid.Parse(Id), outcome.Value!.Id);
        Assert.Equal(new DateOnly(2027, 3, 1), outcome.Value.ExamDate);
    }

    // Planning's validation problem names each field, the case of the key doesn't matter.
    [Fact]
    public async Task Create_Rejected_NamesTheMessagePerField()
    {
        var goals = Goals([], new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent($$$"""
                {"status":400,"traceId":"{{{TraceId}}}","errors":{"Name":["Gib dem Lernziel einen Namen."],
                 "examDate":["Der Prüfungstermin darf nicht in der Vergangenheit liegen."],"Unknown":["?"]}}
                """, Encoding.UTF8, "application/problem+json"),
        });

        var outcome = await goals.CreateAsync(Bio, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal(
            new Dictionary<string, string>
            {
                [LearningGoalValues.Fields.Name] = "Gib dem Lernziel einen Namen.",
                [LearningGoalValues.Fields.ExamDate] = "Der Prüfungstermin darf nicht in der Vergangenheit liegen.",
            },
            outcome.FieldErrors);
    }

    [Fact]
    public async Task Create_BadRequestWithoutFields_Fails()
    {
        var outcome = await Goals([], Problem(HttpStatusCode.BadRequest)).CreateAsync(Bio, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    [Fact]
    public async Task List_ReadsTheGoals()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();
        var goals = Goals(requests, Answer(HttpStatusCode.OK, $$"""
            [{"id":"{{Id}}","kind":"FreeLearning","name":"C#","examType":null,"examDate":null,"level":null,
              "outputLanguage":"en","goal":null,"createdAt":"2026-11-12T10:00:00Z","inPlan":true}]
            """));

        var outcome = await goals.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, Assert.Single(requests).Request.Method);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var goal = Assert.Single(outcome.Value!);
        Assert.Equal(("FreeLearning", "C#", "en", true), (goal.Kind, goal.Name, goal.OutputLanguage, goal.InPlan));
    }

    [Fact]
    public async Task SignedOut_IsSaid()
    {
        var create = Goals([], new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var list = Goals([], new HttpResponseMessage(HttpStatusCode.Unauthorized));

        Assert.Equal(CallResult.SignedOut, (await create.CreateAsync(Bio, TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CallResult.SignedOut, (await list.ListAsync(TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task ServerError_FailsWithTheTrace()
    {
        var outcome = await Goals([], Problem(HttpStatusCode.ServiceUnavailable)).CreateAsync(Bio, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    [Fact]
    public async Task NoConnection_Fails()
    {
        var goals = new HttpLearningGoals(Client((_, _) => throw new HttpRequestException("gone")));

        Assert.Equal(CallResult.Failed, (await goals.CreateAsync(Bio, TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CallResult.Failed, (await goals.ListAsync(TestContext.Current.CancellationToken)).Result);
    }

    private static HttpLearningGoals Goals(List<(HttpRequestMessage Request, string Body)> requests, HttpResponseMessage answer) =>
        new(Client(async (request, cancellationToken) =>
        {
            requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return answer;
        }));

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) =>
        new(new Handler(answer)) { BaseAddress = new Uri("https://edvaniq.test/") };

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode status) =>
        new(status)
        {
            Content = new StringContent($$"""{"status":{{(int)status}},"traceId":"{{TraceId}}"}""", Encoding.UTF8, "application/problem+json"),
        };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}