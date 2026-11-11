using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Today's plan through the web host: the path, how Planning's answer is read, and how errors come back.
public sealed class HttpTodayPlanTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Learn = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string Review = "6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";
    private const string Shortened = "7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e";
    private const string NotReady = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";

    [Fact]
    public async Task Get_ReadsBlocksAndHints()
    {
        var requests = new List<HttpRequestMessage>();
        var plans = Plans(requests, Answer(HttpStatusCode.OK, $$"""
            {"studyDay":"2026-11-11","learningGoalId":"00000000-0000-0000-0000-000000000000",
             "blocks":[{"topicId":"{{Review}}","minutes":10,"kind":"review"},{"topicId":"{{Learn}}","minutes":20,"kind":"learn"}],
             "emptyReason":null,"masteryAvailable":false,"shortenedTopicIds":["{{Shortened}}"],"questionPoolsAvailable":true,
             "notReadyTopicIds":["{{NotReady}}"]}
            """));

        var outcome = await plans.GetAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/plan/today"), request.RequestUri);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var plan = outcome.Value!;
        Assert.Equal(new DateOnly(2026, 11, 11), plan.StudyDay);
        Assert.Equal(
            [new PlanBlock(Guid.Parse(Review), 10, BlockKind.Review), new PlanBlock(Guid.Parse(Learn), 20, BlockKind.Learn)],
            plan.Blocks);
        Assert.Null(plan.Empty);
        Assert.False(plan.MasteryAvailable);
        Assert.Equal([Guid.Parse(Shortened)], plan.ShortenedTopicIds);
        Assert.Equal([Guid.Parse(NotReady)], plan.NotReadyTopicIds);
    }

    [Theory]
    [InlineData("noTopics", PlanEmpty.NoTopics)]
    [InlineData("questionsPending", PlanEmpty.QuestionsPending)]
    [InlineData("allMastered", PlanEmpty.AllMastered)]
    [InlineData("somethingNew", null)]
    [InlineData(null, null)]
    public async Task Get_ReadsTheEmptyReason(string? reason, PlanEmpty? expected)
    {
        var json = reason is null ? "null" : $"\"{reason}\"";
        var plans = Plans([], Answer(HttpStatusCode.OK, $$"""
            {"studyDay":"2026-11-11","blocks":[],"emptyReason":{{json}},"masteryAvailable":true,"shortenedTopicIds":[],
             "questionPoolsAvailable":true,"notReadyTopicIds":[]}
            """));

        var outcome = await plans.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal(expected, outcome.Value!.Empty);
        Assert.Empty(outcome.Value.Blocks);
    }

    // Planning may add kinds and fields later, the plan still comes through.
    [Fact]
    public async Task Get_UnknownKindAndMissingLists_StillGetThrough()
    {
        var plans = Plans([], Answer(HttpStatusCode.OK, $$"""
            {"studyDay":"2026-11-11","blocks":[{"topicId":"{{Learn}}","minutes":5,"kind":"exam","reason":"due"}],
             "masteryAvailable":true}
            """));

        var outcome = await plans.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal([new PlanBlock(Guid.Parse(Learn), 5, null)], outcome.Value!.Blocks);
        Assert.Empty(outcome.Value.ShortenedTopicIds);
        Assert.Empty(outcome.Value.NotReadyTopicIds);
    }

    [Fact]
    public async Task Get_Unauthorized_IsSignedOut()
    {
        var outcome = await Plans([], Problem(HttpStatusCode.Unauthorized)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.SignedOut, outcome.Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Get_Error_FailsWithTheTrace(HttpStatusCode status)
    {
        var outcome = await Plans([], Problem(status)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    [Theory]
    [InlineData("""{"studyDay":"2026-11-11"}""")]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("null")]
    public async Task Get_AnswerThatIsNoPlan_Fails(string body)
    {
        var outcome = await Plans([], Answer(HttpStatusCode.OK, body)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task Get_NoConnection_Fails()
    {
        var plans = new HttpTodayPlan(Client((_, _) => throw new HttpRequestException("gone")));

        var outcome = await plans.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static HttpTodayPlan Plans(List<HttpRequestMessage> requests, HttpResponseMessage answer) =>
        new(Client((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(answer);
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