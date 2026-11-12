using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The week through the web host (#379): the path, how Planning's answer is read, and how errors come back.
public sealed class HttpWeekPlanTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Learn = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string Shortened = "7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e";

    [Fact]
    public async Task Get_ReadsDaysAndHints()
    {
        var requests = new List<HttpRequestMessage>();
        var plans = Plans(requests, Answer(HttpStatusCode.OK, $$"""
            {"learningGoalId":"00000000-0000-0000-0000-000000000000",
             "days":[{"studyDay":"2026-11-11","blocks":[{"topicId":"{{Learn}}","minutes":20,"kind":"learn","id":"{{Learn}}","done":false}]},
                     {"studyDay":"2026-11-12","blocks":[]}],
             "emptyReason":null,"masteryAvailable":false,"shortenedTopicIds":["{{Shortened}}"],"questionPoolsAvailable":true,
             "notReadyTopicIds":[]}
            """));

        var outcome = await plans.GetAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/plan/week"), request.RequestUri);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var week = outcome.Value!;
        Assert.Equal([new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12)], week.Days.Select(day => day.StudyDay));
        Assert.Equal([new PlanBlock(Guid.Parse(Learn), 20, BlockKind.Learn)], week.Days[0].Blocks);
        Assert.Empty(week.Days[1].Blocks);
        Assert.Null(week.Empty);
        Assert.False(week.MasteryAvailable);
        Assert.Equal([Guid.Parse(Shortened)], week.ShortenedTopicIds);
    }

    [Theory]
    [InlineData("noTopics", PlanEmpty.NoTopics)]
    [InlineData("somethingNew", null)]
    public async Task Get_ReadsTheEmptyReason(string reason, PlanEmpty? expected)
    {
        var plans = Plans([], Answer(HttpStatusCode.OK, $$"""
            {"days":[{"studyDay":"2026-11-11","blocks":[]}],"emptyReason":"{{reason}}","masteryAvailable":true}
            """));

        var outcome = await plans.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal(expected, outcome.Value!.Empty);
        Assert.Empty(outcome.Value.ShortenedTopicIds);
    }

    [Fact]
    public async Task Get_Unauthorized_IsSignedOut()
    {
        var outcome = await Plans([], Problem(HttpStatusCode.Unauthorized)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.SignedOut, outcome.Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Get_Error_FailsWithTheTrace(HttpStatusCode status)
    {
        var outcome = await Plans([], Problem(status)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    // Without a single day there is not even today to show.
    [Theory]
    [InlineData("""{"masteryAvailable":true}""")]
    [InlineData("""{"days":[],"masteryAvailable":true}""")]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("null")]
    public async Task Get_AnswerThatIsNoWeek_Fails(string body)
    {
        var outcome = await Plans([], Answer(HttpStatusCode.OK, body)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static HttpWeekPlan Plans(List<HttpRequestMessage> requests, HttpResponseMessage answer) =>
        new(new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            return answer;
        }))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
        });

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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