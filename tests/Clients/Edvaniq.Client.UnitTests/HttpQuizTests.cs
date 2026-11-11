using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Questions and grades through the web host: what goes out, how answers and errors come back.
public sealed class HttpQuizTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string TopicId = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string AttemptId = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";
    private const string QuestionId = "6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";

    private const string AskedJson =
        $$"""{"id":"{{AttemptId}}","topicId":"{{TopicId}}","questionId":"{{QuestionId}}","text":"Frage?","options":["A","B","C"],"difficulty":2}""";

    private const string GradedJson =
        $$"""
        {"id":"{{AttemptId}}","topicId":"{{TopicId}}","questionId":"{{QuestionId}}","option":0,"correct":false,"answer":1,
         "explanation":"Weil.","responseTimeMs":4200,"answeredAt":"2026-11-11T09:00:00.123456Z"}
        """;

    [Fact]
    public async Task Ask_PostsTheTopic_AndReadsTheQuestion()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var quiz = Quiz(requests, Answer(HttpStatusCode.Created, AskedJson));

        var outcome = await quiz.AskAsync(Guid.Parse(TopicId), TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/assessment/attempts"), request.RequestUri);
        Assert.Equal($$"""{"topicId":"{{TopicId}}"}""", body);
        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal(Guid.Parse(AttemptId), outcome.Value!.Id);
        Assert.Equal("Frage?", outcome.Value.Text);
        Assert.Equal(["A", "B", "C"], outcome.Value.Options);
    }

    [Fact]
    public async Task Answer_PostsOptionAndTime_ToTheAttempt()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var quiz = Quiz(requests, Answer(HttpStatusCode.OK, GradedJson));

        var outcome = await quiz.AnswerAsync(Guid.Parse(AttemptId), 0, 4200, TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri($"https://edvaniq.test/api/assessment/attempts/{AttemptId}/answer"), request.RequestUri);
        Assert.Equal("""{"option":0,"responseTimeMs":4200}""", body);
        Assert.Equal(new GradedAnswer(Guid.Parse(AttemptId), 0, false, 1, "Weil.", 4200), outcome.Value);
    }

    // Showing the solution goes the same way as an answer, the grade has no option then.
    [Fact]
    public async Task Reveal_PostsShowSolutionAndTime_ToTheAttempt()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var revealed = $$"""
            {"id":"{{AttemptId}}","topicId":"{{TopicId}}","questionId":"{{QuestionId}}","option":null,"options":null,"revealed":true,
             "correct":false,"answer":1,"answers":[1],"explanation":"Weil.","responseTimeMs":4200,"answeredAt":"2026-11-11T09:00:00.123456Z"}
            """;
        var quiz = Quiz(requests, Answer(HttpStatusCode.OK, revealed));

        var outcome = await quiz.RevealAsync(Guid.Parse(AttemptId), 4200, TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(new Uri($"https://edvaniq.test/api/assessment/attempts/{AttemptId}/answer"), request.RequestUri);
        Assert.Equal("""{"showSolution":true,"responseTimeMs":4200}""", body);
        Assert.Equal(new GradedAnswer(Guid.Parse(AttemptId), null, false, 1, "Weil.", 4200, Revealed: true), outcome.Value);
    }

    // A grade with no option is only fine when the solution was shown.
    [Fact]
    public async Task GradeWithoutOption_AndNotRevealed_Fails()
    {
        var quiz = Quiz([], Answer(HttpStatusCode.OK, GradedJson.Replace("\"option\":0", "\"option\":null", StringComparison.Ordinal)));

        var outcome = await quiz.AnswerAsync(Guid.Parse(AttemptId), 0, 1, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CallResult.SignedOut)]
    [InlineData(HttpStatusCode.NotFound, CallResult.NotFound)]
    [InlineData(HttpStatusCode.Conflict, CallResult.NotReady)]
    [InlineData(HttpStatusCode.BadRequest, CallResult.Failed)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CallResult.Failed)]
    [InlineData(HttpStatusCode.Forbidden, CallResult.Failed)]
    public async Task Status_BecomesItsResult(HttpStatusCode status, CallResult expected)
    {
        var quiz = Quiz([], Problem(status));

        var outcome = await quiz.AskAsync(Guid.Parse(TopicId), TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.Result);
    }

    [Fact]
    public async Task Failure_NamesItsTrace()
    {
        var quiz = Quiz([], Problem(HttpStatusCode.ServiceUnavailable));

        var outcome = await quiz.AnswerAsync(Guid.Parse(AttemptId), 0, 1, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    // A question that is no single choice or an answer that is no question at all did not really get through.
    [Theory]
    [InlineData($$"""{"id":"{{AttemptId}}","topicId":"{{TopicId}}","questionId":"{{QuestionId}}","text":"Frage?","options":["A"],"difficulty":2}""")]
    [InlineData($$"""{"id":"{{AttemptId}}","topicId":"{{TopicId}}","questionId":"{{QuestionId}}","options":["A","B"],"difficulty":2}""")]
    [InlineData("<html>proxy</html>")]
    public async Task UnusableQuestion_Fails(string body)
    {
        var quiz = Quiz([], Answer(HttpStatusCode.Created, body));

        var outcome = await quiz.AskAsync(Guid.Parse(TopicId), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task NoConnection_Fails()
    {
        var quiz = new HttpQuiz(Client((_, _) => throw new HttpRequestException("down")));

        var outcome = await quiz.AnswerAsync(Guid.Parse(AttemptId), 0, 1, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static HttpQuiz Quiz(List<(HttpRequestMessage Request, string? Body)> requests, params HttpResponseMessage[] answers)
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