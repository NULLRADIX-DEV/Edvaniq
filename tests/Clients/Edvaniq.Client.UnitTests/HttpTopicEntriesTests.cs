using System.Net;
using System.Text;
using System.Text.Json;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// #317: a topic entered by hand goes to Knowledge through the web host: the path, what is sent, and how Knowledge's
// answers come back.
public sealed class HttpTopicEntriesTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly Guid GoalId = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");

    [Fact]
    public async Task Add_SendsNameAndGoal_AndCreatedIsAdded()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();
        var entries = Entries(requests, Answer(HttpStatusCode.Created, $$"""{"id":"{{Guid.NewGuid()}}","name":"Zellatmung"}"""));

        var outcome = await entries.AddAsync(GoalId, "Zellatmung", TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/knowledge/topics"), request.RequestUri);
        using var sent = JsonDocument.Parse(body);
        Assert.Equal("Zellatmung", sent.RootElement.GetProperty("name").GetString());
        Assert.Equal(GoalId, sent.RootElement.GetProperty("learningGoalId").GetGuid());
        Assert.False(sent.RootElement.TryGetProperty("parentId", out _));
        Assert.Equal(CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: false)), outcome);
    }

    // A topic of the same name is what the user wanted, nothing to report.
    [Fact]
    public async Task Add_NameTaken_CountsAsAdded()
    {
        var outcome = await Entries([], Problem(HttpStatusCode.Conflict)).AddAsync(GoalId, "Zellatmung", TestContext.Current.CancellationToken);

        Assert.Equal(CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: true)), outcome);
    }

    [Fact]
    public async Task Add_RejectedName_SaysWhy()
    {
        var entries = Entries([], new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                $$$"""{"status":400,"traceId":"{{{TraceId}}}","errors":{"name":["Der Name ist zu lang. Erlaubt sind höchstens 80 Zeichen."]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        });

        var outcome = await entries.AddAsync(GoalId, "Zellatmung", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal(["Der Name ist zu lang. Erlaubt sind höchstens 80 Zeichen."], outcome.Messages);
    }

    // Not the name's fault, the learning goal for instance: the user can't fix it at the field.
    [Fact]
    public async Task Add_BadRequestWithoutName_Fails()
    {
        var entries = Entries([], new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                $$$"""{"status":400,"traceId":"{{{TraceId}}}","errors":{"LearningGoalId":["Gib entweder ein Lernziel oder ein übergeordnetes Thema an."]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        });

        var outcome = await entries.AddAsync(GoalId, "Zellatmung", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CallResult.SignedOut)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CallResult.Failed)]
    [InlineData(HttpStatusCode.NotFound, CallResult.Failed)]
    public async Task Add_OtherAnswers(HttpStatusCode status, CallResult result)
    {
        var outcome = await Entries([], Problem(status)).AddAsync(GoalId, "Zellatmung", TestContext.Current.CancellationToken);

        Assert.Equal(result, outcome.Result);
    }

    [Fact]
    public async Task Add_WithoutConnection_Fails()
    {
        var entries = new HttpTopicEntries(Client((_, _) => throw new HttpRequestException("gone")));

        var outcome = await entries.AddAsync(GoalId, "Zellatmung", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    // #295: each change of the directory with its method, path and body.
    [Fact]
    public async Task Changes_GoToTheirRoutes()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();
        var entries = Entries(requests, () => Answer(HttpStatusCode.OK, Edited));
        var cancel = TestContext.Current.CancellationToken;

        await entries.CreateTopicAsync(GoalId, "Zelle", cancel);
        await entries.CreateSubtopicAsync(TopicId, "Zellkern", cancel);
        await entries.RenameAsync(TopicId, "Zellbiologie", cancel);
        await entries.HideAsync(TopicId, cancel);
        await entries.ShowAsync(TopicId, cancel);

        Assert.Equal(
            [
                ("POST", "/api/knowledge/topics", $$"""{"name":"Zelle","learningGoalId":"{{GoalId}}"}"""),
                ("POST", "/api/knowledge/topics", $$"""{"name":"Zellkern","parentId":"{{TopicId}}"}"""),
                ("PATCH", $"/api/knowledge/topics/{TopicId}", """{"name":"Zellbiologie"}"""),
                ("PUT", $"/api/knowledge/topics/{TopicId}/hidden", ""),
                ("DELETE", $"/api/knowledge/topics/{TopicId}/hidden", ""),
            ],
            requests.Select(sent => (sent.Request.Method.Method, sent.Request.RequestUri!.AbsolutePath, sent.Body)));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    public async Task Change_ThatWentThrough_GivesTheTopic(HttpStatusCode status)
    {
        var outcome = await Entries([], () => Answer(status, Edited)).HideAsync(TopicId, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal(new EditedTopic(TopicId, "Zelle", null, true), outcome.Value!.Topic);
    }

    // The conflicts by their code only, never by Knowledge's text. Without a known code it is Other.
    [Theory]
    [InlineData("nameTaken", TopicConflict.NameTaken)]
    [InlineData("parentHidden", TopicConflict.ParentHidden)]
    [InlineData("somethingNew", TopicConflict.Other)]
    [InlineData(null, TopicConflict.Other)]
    public async Task Change_Conflict_IsToldApartByItsCode(string? code, TopicConflict conflict)
    {
        var outcome = await Entries([], () => Conflict(code)).RenameAsync(TopicId, "Zelle", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal(conflict, outcome.Value!.Conflict);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    // Busy is tried once more, not again and again.
    [Fact]
    public async Task Change_Busy_IsTriedOnceMore()
    {
        var requests = new List<(HttpRequestMessage Request, string Body)>();

        var outcome = await Entries(requests, () => Conflict("busy")).ShowAsync(TopicId, TestContext.Current.CancellationToken);

        Assert.Equal(2, requests.Count);
        Assert.Equal(TopicConflict.Busy, outcome.Value!.Conflict);
    }

    [Fact]
    public async Task Change_BusyOnce_GoesThroughTheSecondTime()
    {
        var answers = new Queue<HttpResponseMessage>([Conflict("busy"), Answer(HttpStatusCode.OK, Edited)]);

        var outcome = await Entries([], answers.Dequeue).HideAsync(TopicId, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
    }

    [Fact]
    public async Task Change_RejectedName_SaysWhyAtTheField()
    {
        var entries = Entries([], () => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"status":400,"errors":{"Name":["Der Name ist leer. Gib dem Thema einen Namen."]}}""", Encoding.UTF8, "application/problem+json"),
        });

        var outcome = await entries.CreateSubtopicAsync(TopicId, " ", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal("Der Name ist leer. Gib dem Thema einen Namen.", outcome.FieldErrors!["Name"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, CallResult.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, CallResult.SignedOut)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CallResult.Failed)]
    public async Task Change_OtherAnswers(HttpStatusCode status, CallResult result)
    {
        var outcome = await Entries([], () => Problem(status)).RenameAsync(TopicId, "Zelle", TestContext.Current.CancellationToken);

        Assert.Equal(result, outcome.Result);
    }

    [Fact]
    public async Task Change_WithoutConnection_Fails()
    {
        var entries = new HttpTopicEntries(Client((_, _) => throw new HttpRequestException("gone")), TimeSpan.Zero);

        var outcome = await entries.HideAsync(TopicId, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static readonly Guid TopicId = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    private static string Edited =>
        $$"""{"id":"{{TopicId}}","learningGoalId":"{{GoalId}}","parentId":null,"name":"Zelle","documentId":null,"position":0,"hidden":true}""";

    private static HttpResponseMessage Conflict(string? code) =>
        new(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                code is null
                    ? $$"""{"status":409,"detail":"Etwas.","traceId":"{{TraceId}}"}"""
                    : $$"""{"status":409,"detail":"Etwas.","code":"{{code}}","traceId":"{{TraceId}}"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

    private static HttpTopicEntries Entries(List<(HttpRequestMessage Request, string Body)> requests, Func<HttpResponseMessage> answer) =>
        new(
            Client(async (request, cancellationToken) =>
            {
                requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
                return answer();
            }),
            TimeSpan.Zero);

    private static HttpTopicEntries Entries(List<(HttpRequestMessage Request, string Body)> requests, HttpResponseMessage answer) =>
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