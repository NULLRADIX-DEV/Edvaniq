using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The summary of a topic through the web host (#346): the paths, how Content's answer is read, and its problems.
public sealed class HttpTopicSummariesTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string TopicId = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string DocumentId = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";

    private static string SummaryJson(string status, string length = "standard", string? text = null, string? reason = null) =>
        $$"""
        {"topicId":"{{TopicId}}","length":"{{length}}","status":"{{status}}","text":{{Json(text)}},"failureCode":null,
         "failureReason":{{Json(reason)}},"aiGenerated":true,"model":"m","promptVersion":"summary-1",
         "createdAt":"2026-11-15T09:00:00Z","readyAt":null,
         "sources":[{"documentId":"{{DocumentId}}","documentTitle":"Skript","pageFrom":3,"pageTo":4}]}
        """;

    [Theory]
    [InlineData(SummaryLength.Short, "short")]
    [InlineData(SummaryLength.Standard, "standard")]
    [InlineData(SummaryLength.Detailed, "detailed")]
    public async Task Request_PostsToTheLength_AndReadsAPendingOne(SummaryLength length, string name)
    {
        var requests = new List<HttpRequestMessage>();
        var summaries = Summaries(requests, Answer(HttpStatusCode.Accepted, SummaryJson("pending", name)));

        var outcome = await summaries.RequestAsync(Guid.Parse(TopicId), length, TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri($"https://edvaniq.test/api/content/topics/{TopicId}/summaries/{name}"), request.RequestUri);
        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal((length, SummaryStatus.Pending, (string?)null), (outcome.Value!.Length, outcome.Value.Status, outcome.Value.Text));
    }

    [Fact]
    public async Task Get_ReadsAReadyOne_WithItsSources()
    {
        var requests = new List<HttpRequestMessage>();
        var summaries = Summaries(requests, Answer(HttpStatusCode.OK, SummaryJson("ready", text: "Erster Absatz.\nZweiter Absatz.")));

        var outcome = await summaries.GetAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, Assert.Single(requests).Method);
        var summary = outcome.Value!;
        Assert.Equal((SummaryStatus.Ready, "Erster Absatz.\nZweiter Absatz.", true), (summary.Status, summary.Text, summary.AiGenerated));
        Assert.Equal([new SummarySource(Guid.Parse(DocumentId), "Skript", 3, 4)], summary.Sources);
    }

    [Theory]
    [InlineData("Die KI hat abgelehnt.", "Die KI hat abgelehnt.")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public async Task Failed_CarriesItsReason_IfThereIsOne(string? reason, string? expected)
    {
        var outcome = await Summaries([], Answer(HttpStatusCode.OK, SummaryJson("failed", reason: reason)))
            .GetAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal((SummaryStatus.Failed, expected), (outcome.Value!.Status, outcome.Value.FailureReason));
    }

    [Fact]
    public async Task NotFound_IsNotFound()
    {
        var outcome = await Summaries([], Problem(HttpStatusCode.NotFound, """{"status":404}"""))
            .RequestAsync(Guid.Parse(TopicId), SummaryLength.Short, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task NoMaterial_IsNotReady_WithContentsSentence()
    {
        const string sentence = "Zu diesem Thema gibt es noch kein Material, aus dem eine Zusammenfassung entstehen kann.";
        var outcome = await Summaries([], Problem(HttpStatusCode.Conflict, $$"""{"status":409,"title":"{{sentence}}"}"""))
            .RequestAsync(Guid.Parse(TopicId), SummaryLength.Short, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.NotReady, outcome.Result);
        Assert.Equal([sentence], outcome.Messages);
    }

    // The monthly budget (429) and a paused AI or unreachable topics (503) hold it up for now, with Content's sentence.
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Zusammenfassungen sind gerade pausiert. Versuch es später noch einmal.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Die Themen sind gerade nicht erreichbar. Versuch es gleich noch einmal.")]
    public async Task LimitOrPause_IsLimited_WithContentsSentence(HttpStatusCode status, string sentence)
    {
        var outcome = await Summaries([], Problem(status, $$"""{"status":{{(int)status}},"title":"{{sentence}}","traceId":"{{TraceId}}"}"""))
            .RequestAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Limited, outcome.Result);
        Assert.Equal([sentence], outcome.Messages);
        Assert.Null(outcome.TraceId);
    }

    // A 503 without a sentence comes from the gateway or a proxy: the call didn't get through.
    [Theory]
    [InlineData("application/problem+json", """{"status":503,"traceId":"4bf92f3577b34da6a3ce929d0e0e4736"}""")]
    [InlineData("text/html", "<html>503 Service Unavailable</html>")]
    public async Task ServiceUnavailableWithoutASentence_Fails(string type, string body)
    {
        var outcome = await Summaries([], new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(body, Encoding.UTF8, type) })
            .RequestAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task ConflictWithoutASentence_Fails()
    {
        var outcome = await Summaries([], Problem(HttpStatusCode.Conflict, """{"status":409}"""))
            .RequestAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task Unauthorized_IsSignedOut()
    {
        var outcome = await Summaries([], Problem(HttpStatusCode.Unauthorized, """{"status":401}"""))
            .GetAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.SignedOut, outcome.Result);
    }

    [Fact]
    public async Task ServerError_FailsWithTheTrace()
    {
        var outcome = await Summaries([], Problem(HttpStatusCode.BadGateway, $$"""{"status":502,"traceId":"{{TraceId}}"}"""))
            .GetAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal((CallResult.Failed, TraceId), (outcome.Result, outcome.TraceId));
    }

    // Without topic, length or status, with a length or status this app doesn't know, or ready without text, there is no
    // summary to show.
    [Theory]
    [InlineData("""{"length":"standard","status":"ready","text":"T"}""")]
    [InlineData($$"""{"topicId":"{{TopicId}}","status":"ready","text":"T"}""")]
    [InlineData($$"""{"topicId":"{{TopicId}}","length":"huge","status":"ready","text":"T"}""")]
    [InlineData($$"""{"topicId":"{{TopicId}}","length":"standard","status":"cooking"}""")]
    [InlineData($$"""{"topicId":"{{TopicId}}","length":"standard","status":"ready","text":" "}""")]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("null")]
    public async Task AnswerThatIsNoSummary_Fails(string body)
    {
        var outcome = await Summaries([], Answer(HttpStatusCode.OK, body))
            .GetAsync(Guid.Parse(TopicId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static string Json(string? value) => value is null ? "null" : System.Text.Json.JsonSerializer.Serialize(value);

    private static HttpTopicSummaries Summaries(List<HttpRequestMessage> requests, HttpResponseMessage answer) =>
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

    private static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}