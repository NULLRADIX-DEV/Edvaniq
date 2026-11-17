using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The check questions of a new topic through the web host (#358): what goes out, both statuses and the errors.
public sealed class HttpCheckQuestionsTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string TopicId = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string SessionId = "3a4b5c6d-7e8f-4a9b-8c0d-1e2f3a4b5c63";
    private const string FirstAttempt = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";
    private const string SecondAttempt = "7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02";
    private const string DocumentId = "8d2e3f4a-5b6c-4d7e-8f9a-0b1c2d3e4f03";
    private const string SectionId = "9e3f4a5b-6c7d-4e8f-9a0b-1c2d3e4f5a04";

    // As Assessment's CheckResponse names the fields.
    private const string ReadyJson =
        $$$"""
        {"topicId":"{{{TopicId}}}","status":"ready","questions":[
         {"id":"{{{FirstAttempt}}}","topicId":"{{{TopicId}}}","questionId":"{{{FirstAttempt}}}","type":"trueFalse","text":"Wahr?",
          "options":["Wahr","Falsch"],"difficulty":1,
          "source":{"documentId":"{{{DocumentId}}}","sectionId":"{{{SectionId}}}","model":"m","promptVersion":"questions-2"}},
         {"id":"{{{SecondAttempt}}}","topicId":"{{{TopicId}}}","questionId":"{{{SecondAttempt}}}","type":"multipleChoice","text":"Welche?",
          "options":["A","B","C"],"difficulty":2,
          "source":{"documentId":"{{{DocumentId}}}","sectionId":"{{{SectionId}}}","model":"m","promptVersion":"questions-2"}}]}
        """;

    private const string NotReadyJson = $$"""{"topicId":"{{TopicId}}","status":"notReady","questions":[]}""";

    [Fact]
    public async Task Start_PostsTopicAndSession_AndReadsTheQuestions()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var checks = Checks(requests, Answer(HttpStatusCode.OK, ReadyJson));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), Guid.Parse(SessionId), TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/assessment/check-questions"), request.RequestUri);
        Assert.Equal($$"""{"topicId":"{{TopicId}}","sessionId":"{{SessionId}}"}""", body);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var check = outcome.Value!;
        Assert.True(check.Ready);
        Assert.Equal(Guid.Parse(TopicId), check.TopicId);
        Assert.Collection(
            check.Questions,
            first =>
            {
                Assert.Equal(Guid.Parse(FirstAttempt), first.Id);
                Assert.Equal(QuestionTypes.TrueFalse, first.Type);
            },
            second =>
            {
                Assert.Equal(["A", "B", "C"], second.Options);
                Assert.Equal(QuestionTypes.MultipleChoice, second.Type);
                Assert.Equal(Guid.Parse(DocumentId), second.Source!.DocumentId);
            });
    }

    [Fact]
    public async Task WithoutASession_SendsNone()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var checks = Checks(requests, Answer(HttpStatusCode.OK, ReadyJson));

        await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal($$"""{"topicId":"{{TopicId}}","sessionId":null}""", Assert.Single(requests).Body);
    }

    // No pool yet is a status, not an error: the unit goes on with the summary alone.
    [Fact]
    public async Task NotReady_IsOk_WithoutQuestions()
    {
        var checks = Checks([], Answer(HttpStatusCode.OK, NotReadyJson));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.False(outcome.Value!.Ready);
        Assert.Empty(outcome.Value.Questions);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CallResult.SignedOut)]
    [InlineData(HttpStatusCode.NotFound, CallResult.NotFound)]
    [InlineData(HttpStatusCode.BadGateway, CallResult.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CallResult.Failed)]
    public async Task Errors_AreNamed(HttpStatusCode status, CallResult expected)
    {
        var checks = Checks([], Problem(status));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.Result);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task Failed_KeepsTheTraceId()
    {
        var checks = Checks([], Problem(HttpStatusCode.InternalServerError));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal(TraceId, outcome.TraceId);
    }

    // A status the app doesn't know, "ready" without questions, a question it couldn't show or no JSON at all didn't
    // really get through.
    [Theory]
    [InlineData($$"""{"topicId":"{{TopicId}}","status":"later","questions":[]}""")]
    [InlineData($$"""{"topicId":"{{TopicId}}","status":"ready","questions":[]}""")]
    [InlineData($$"""{"topicId":"{{TopicId}}","questions":[]}""")]
    [InlineData("{\"topicId\":")]
    [InlineData("<html></html>")]
    public async Task BrokenAnswer_Fails(string body)
    {
        var checks = Checks([], Answer(HttpStatusCode.OK, body));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task QuestionWithOneOption_Fails()
    {
        var checks = Checks([], Answer(HttpStatusCode.OK, ReadyJson.Replace("[\"A\",\"B\",\"C\"]", "[\"A\"]", StringComparison.Ordinal)));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task NoConnection_Fails()
    {
        var checks = new HttpQuiz(Client((_, _) => throw new HttpRequestException("down")));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task Timeout_Fails()
    {
        var checks = new HttpQuiz(Client((_, _) => throw new TaskCanceledException("slow", new TimeoutException())));

        var outcome = await checks.StartAsync(Guid.Parse(TopicId), null, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static ICheckQuestions Checks(List<(HttpRequestMessage Request, string? Body)> requests, params HttpResponseMessage[] answers)
    {
        var queue = new Queue<HttpResponseMessage>(answers);
        return new HttpQuiz(Client(async (request, cancellationToken) =>
        {
            requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return queue.Dequeue();
        }));
    }

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