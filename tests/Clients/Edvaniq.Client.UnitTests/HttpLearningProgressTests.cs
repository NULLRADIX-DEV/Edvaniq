using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The mastery of the user's topics through the web host: the path, how LearningEngine's answer is read, and errors.
public sealed class HttpLearningProgressTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Topic = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string Untouched = "7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e";
    private const string Answer = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";

    [Fact]
    public async Task List_ReadsMasteryStateAndNewestAnswer()
    {
        var requests = new List<HttpRequestMessage>();
        var progress = Progress(requests, Ok($$"""
            {"ruleVersion":2,"topics":[
             {"topicId":"{{Topic}}","mastery":62,"state":"building","lastAnswerId":"{{Answer}}"},
             {"topicId":"{{Untouched}}","mastery":50,"state":"new","lastAnswerId":null}]}
            """));

        var outcome = await progress.ListAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/learningengine/mastery"), request.RequestUri);
        Assert.Equal(
            [
                new TopicProgress(Guid.Parse(Topic), 62, TopicState.Building, Guid.Parse(Answer)),
                new TopicProgress(Guid.Parse(Untouched), 50, TopicState.New, null),
            ],
            outcome.Value);
    }

    [Theory]
    [InlineData("weakness", TopicState.Weakness)]
    [InlineData("secure", TopicState.Secure)]
    [InlineData("mastered", null)]
    public async Task List_TakesTheStateAsItIs(string state, TopicState? expected)
    {
        var progress = Progress([], Ok($$"""{"ruleVersion":2,"topics":[{"topicId":"{{Topic}}","mastery":40,"state":"{{state}}"}]}"""));

        var outcome = await progress.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(outcome.Value!).State);
    }

    [Fact]
    public async Task List_Unauthorized_IsSignedOut()
    {
        var outcome = await Progress([], Problem(HttpStatusCode.Unauthorized)).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.SignedOut, outcome.Result);
    }

    [Fact]
    public async Task List_Error_FailsWithTheTrace()
    {
        var outcome = await Progress([], Problem(HttpStatusCode.BadGateway)).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    [Theory]
    [InlineData("""{"ruleVersion":2}""")]
    [InlineData("<html>Bad Gateway</html>")]
    public async Task List_AnswerThatIsNoList_Fails(string body)
    {
        var outcome = await Progress([], Ok(body)).ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static HttpLearningProgress Progress(List<HttpRequestMessage> requests, HttpResponseMessage answer) =>
        new(new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            return answer;
        }))
        { BaseAddress = new Uri("https://edvaniq.test/") });

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode status) =>
        new(status)
        {
            Content = new StringContent($$"""{"status":{{(int)status}},"traceId":"{{TraceId}}"}""", Encoding.UTF8, "application/problem+json"),
        };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}