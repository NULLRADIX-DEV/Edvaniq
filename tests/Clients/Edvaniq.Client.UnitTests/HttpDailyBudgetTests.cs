using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The daily budget through the web host: what goes out, how answers and errors come back.
public sealed class HttpDailyBudgetTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private const string WeekJson =
        """{"monday":30,"tuesday":30,"wednesday":45,"thursday":30,"friday":30,"saturday":60,"sunday":10,"isDefault":false}""";

    [Fact]
    public async Task Budget_IsAskedFromPlanning_ByWeekday()
    {
        var requests = new List<HttpRequestMessage>();
        var budget = Budget(requests, [], Answer(HttpStatusCode.OK, WeekJson));

        var outcome = await budget.GetAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/daily-budget"), request.RequestUri);
        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal([10, 30, 30, 45, 30, 30, 60], outcome.Value!.Minutes);
        Assert.False(outcome.Value.IsDefault);
        Assert.False(outcome.Value.IsUniform);
        Assert.Equal(30, outcome.Value.Usual);
    }

    // AC1: one value goes out for every weekday.
    [Fact]
    public async Task Set_PutsTheSameMinutesForAllSevenDays()
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var budget = Budget(requests, bodies, Answer(HttpStatusCode.OK, WeekJson.Replace("45", "30", StringComparison.Ordinal)));

        var outcome = await budget.SetAsync(45, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Put, Assert.Single(requests).Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/planning/daily-budget"), requests[0].RequestUri);
        Assert.Equal(
            """{"monday":45,"tuesday":45,"wednesday":45,"thursday":45,"friday":45,"saturday":45,"sunday":45}""",
            Assert.Single(bodies));
        Assert.Equal(CallResult.Ok, outcome.Result);
    }

    [Fact]
    public async Task Default_SaysSo()
    {
        var budget = Budget([], [], Answer(HttpStatusCode.OK,
            """{"monday":60,"tuesday":60,"wednesday":60,"thursday":60,"friday":60,"saturday":60,"sunday":60,"isDefault":true}"""));

        var info = (await budget.GetAsync(TestContext.Current.CancellationToken)).Value!;

        Assert.True(info.IsDefault);
        Assert.True(info.IsUniform);
        Assert.Equal(60, info.Usual);
    }

    // Planning names each day with the same sentence, it comes once.
    [Fact]
    public async Task Refused_GivesPlanningsMessageOnce()
    {
        const string message = "Gib für jeden Tag zwischen 1 und 720 Minuten an.";
        var errors = string.Join(',', DailyBudgetValues.Days.Select(day => $$"""
            "{{char.ToUpperInvariant(day[0]) + day[1..]}}":["{{message}}"]
            """));
        var budget = Budget([], [], Problem(HttpStatusCode.BadRequest, $$"""{"status":400,"errors":{{{errors}}},"traceId":"{{TraceId}}"}"""));

        var outcome = await budget.SetAsync(900, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal([message], outcome.Messages);
    }

    [Theory]
    [InlineData("application/problem+json", "{\"status\":400}")]
    [InlineData("text/html", "<html>Bad Request</html>")]
    public async Task BadRequestWithoutMessages_DidNotGetThrough(string type, string body)
    {
        var budget = Budget([], [], new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body, Encoding.UTF8, type) });

        Assert.Equal(CallResult.Failed, (await budget.SetAsync(30, TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task WithoutALogin_SaysSignedOut()
    {
        var budget = Budget([], [], Problem(HttpStatusCode.Unauthorized, """{"status":401}"""));

        Assert.Equal(CallResult.SignedOut, (await budget.GetAsync(TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task Problem_PassesTheTraceOn()
    {
        var budget = Budget([], [], Problem(HttpStatusCode.ServiceUnavailable, $$"""{"status":503,"traceId":"{{TraceId}}"}"""));

        var outcome = await budget.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal((CallResult.Failed, TraceId), (outcome.Result, outcome.TraceId));
    }

    // An answer without all seven days is no budget.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"monday":30,"isDefault":false}""")]
    [InlineData("null")]
    [InlineData("<html></html>")]
    public async Task AnswerWithoutAWeek_DidNotGetThrough(string body)
    {
        var budget = Budget([], [], Answer(HttpStatusCode.OK, body));

        Assert.Equal(CallResult.Failed, (await budget.GetAsync(TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task NoConnection_DidNotGetThrough()
    {
        var budget = new HttpDailyBudget(Client((_, _) => throw new HttpRequestException("Connection refused")));

        Assert.Equal(CallResult.Failed, (await budget.SetAsync(30, TestContext.Current.CancellationToken)).Result);
    }

    private static HttpDailyBudget Budget(List<HttpRequestMessage> requests, List<string> bodies, params HttpResponseMessage[] answers)
    {
        var queue = new Queue<HttpResponseMessage>(answers);
        return new HttpDailyBudget(Client(async (request, cancellationToken) =>
        {
            requests.Add(request);
            if (request.Content is not null)
            {
                bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return queue.Dequeue();
        }));
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) =>
        new(new Handler(answer)) { BaseAddress = new Uri("https://edvaniq.test/") };

    private static HttpResponseMessage Answer(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}