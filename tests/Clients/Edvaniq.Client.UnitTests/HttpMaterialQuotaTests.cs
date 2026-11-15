using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The daily quotas through the web host (#307): the path, how Content's answer is read, and how errors come back.
public sealed class HttpMaterialQuotaTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task Get_ReadsPagesAndGenerations()
    {
        var requests = new List<HttpRequestMessage>();
        var quotas = Quotas(requests, Answer(HttpStatusCode.OK, """
            {"pagesLeftToday":42,"pagesPerDay":200,"generationsLeftToday":7,"generationsPerDay":20,"resetsAt":"2026-11-16T03:00:00Z"}
            """));

        var outcome = await quotas.GetAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/content/quota"), request.RequestUri);
        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal(new MaterialQuota(42, 200, 7, 20, new DateTime(2026, 11, 16, 3, 0, 0, DateTimeKind.Utc)), outcome.Value);
    }

    [Fact]
    public async Task Get_WithoutGenerationLimit_HasNoGenerations()
    {
        var outcome = await Quotas([], Answer(HttpStatusCode.OK, """
            {"pagesLeftToday":0,"pagesPerDay":200,"generationsLeftToday":null,"generationsPerDay":null,"resetsAt":"2026-11-16T03:00:00Z"}
            """)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal((0, (int?)null), (outcome.Value!.PagesLeftToday, outcome.Value.GenerationsLeftToday));
    }

    [Fact]
    public async Task Get_Unauthorized_IsSignedOut()
    {
        var outcome = await Quotas([], Problem(HttpStatusCode.Unauthorized)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.SignedOut, outcome.Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Get_Error_FailsWithTheTrace(HttpStatusCode status)
    {
        var outcome = await Quotas([], Problem(status)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    [Theory]
    [InlineData("""{"pagesPerDay":200,"resetsAt":"2026-11-16T03:00:00Z"}""")]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("null")]
    public async Task Get_AnswerThatIsNoQuota_Fails(string body)
    {
        var outcome = await Quotas([], Answer(HttpStatusCode.OK, body)).GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task Get_NoConnection_Fails()
    {
        var quotas = new HttpMaterialQuota(new HttpClient(new Handler(_ => throw new HttpRequestException("down")))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
        });

        Assert.Equal(CallResult.Failed, (await quotas.GetAsync(TestContext.Current.CancellationToken)).Result);
    }

    private static HttpMaterialQuota Quotas(List<HttpRequestMessage> requests, HttpResponseMessage answer) =>
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