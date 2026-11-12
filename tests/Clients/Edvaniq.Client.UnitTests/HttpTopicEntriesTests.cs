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