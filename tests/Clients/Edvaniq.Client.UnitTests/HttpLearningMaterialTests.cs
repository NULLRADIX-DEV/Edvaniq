using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Documents and topics through the web host: what goes out, how answers and errors come back.
public sealed class HttpLearningMaterialTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string DocumentId = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";

    private const string DocumentJson =
        $$"""{"id":"{{DocumentId}}","title":"Skript Bio","status":"Analyzing","failureReason":null,"createdAt":"2026-11-11T09:00:00Z"}""";

    [Fact]
    public async Task Documents_AreAskedFromContent_OnTheHost()
    {
        var requests = new List<HttpRequestMessage>();
        var material = Material(requests, Json($"[{DocumentJson}]"));

        var outcome = await material.ListDocumentsAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/content/documents"), request.RequestUri);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var document = Assert.Single(outcome.Value!);
        Assert.Equal(
            new DocumentInfo(Guid.Parse(DocumentId), "Skript Bio", DocumentStatus.Analyzing, new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero)),
            document);
    }

    [Fact]
    public async Task FailedDocument_SaysWhy()
    {
        var material = Material(Json($$"""[{"id":"{{DocumentId}}","title":"T","status":"Failed","failureReason":"Grund.","createdAt":"2026-11-11T09:00:00Z"}]"""));

        var outcome = await material.ListDocumentsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Grund.", Assert.Single(outcome.Value!).FailureReason);
    }

    // The tree of Knowledge, both levels with the document they came from.
    [Fact]
    public async Task Topics_AreAskedFromKnowledge_AsATree()
    {
        var requests = new List<HttpRequestMessage>();
        var material = Material(requests, Json($$"""
            [{"id":"5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50","learningGoalId":"00000000-0000-0000-0000-000000000000",
              "name":"Photosynthese","documentId":"{{DocumentId}}","position":0,
              "subtopics":[{"id":"6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d","name":"Lichtreaktion","documentId":"{{DocumentId}}","position":1}]}]
            """));

        var outcome = await material.ListTopicsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://edvaniq.test/api/knowledge/topics"), Assert.Single(requests).RequestUri);
        var topic = Assert.Single(outcome.Value!);
        Assert.Equal("Photosynthese", topic.Name);
        Assert.Equal("Lichtreaktion", Assert.Single(topic.Subtopics).Name);
        Assert.Equal(Guid.Parse(DocumentId), topic.DocumentId);
        Assert.Equal(Guid.Parse(DocumentId), topic.Subtopics[0].DocumentId);
    }

    // A topic that came another way names no document.
    [Fact]
    public async Task TopicWithoutDocument_ComesFromNone()
    {
        var material = Material(Json("""[{"id":"5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50","name":"Eigenes","subtopics":[]}]"""));

        var topic = Assert.Single((await material.ListTopicsAsync(TestContext.Current.CancellationToken)).Value!);

        Assert.Null(topic.DocumentId);
    }

    [Fact]
    public async Task Paste_PostsTitleAndText_ToContent()
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var material = Material(requests, bodies, Created(DocumentJson));

        var outcome = await material.PasteAsync("Skript", "Text", TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/content/documents"), request.RequestUri);
        Assert.Equal("""{"title":"Skript","text":"Text"}""", Assert.Single(bodies));
        Assert.Equal(CallResult.Ok, outcome.Result);
        Assert.Equal(Guid.Parse(DocumentId), outcome.Value!.Id);
    }

    // 200.000 characters have to get through the proxies' limit of a megabyte: an umlaut stays two bytes, not the six of
    // ü. Quotes and control characters are still escaped, so the body stays valid JSON.
    [Fact]
    public async Task Paste_KeepsUmlautsAsTheyAre()
    {
        var bodies = new List<string>();
        var material = Material([], bodies, Created(DocumentJson));

        await material.PasteAsync(null, "Grüße \"Ä\"\n", TestContext.Current.CancellationToken);

        Assert.Equal("""{"title":null,"text":"Grüße \"Ä\"\n"}""", Assert.Single(bodies));
    }

    [Fact]
    public async Task Paste_OfTheLongestText_StaysBelowAMegabyte()
    {
        var bodies = new List<string>();
        var material = Material([], bodies, Created(DocumentJson));

        await material.PasteAsync(null, new string('ü', HttpLearningMaterial.TextMaxLength), TestContext.Current.CancellationToken);

        Assert.True(Encoding.UTF8.GetByteCount(Assert.Single(bodies)) < 1_000_000);
    }

    // Content's validation problem: its messages, meant to be shown as they are.
    [Fact]
    public async Task Refused_GivesContentsMessages()
    {
        var material = Material(Problem(HttpStatusCode.BadRequest, $$"""
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"Text":["Der Text ist leer."],"Title":["Der Titel ist zu lang."]},"traceId":"{{TraceId}}"}
            """));

        var outcome = await material.PasteAsync(null, " ", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal(["Der Text ist leer.", "Der Titel ist zu lang."], outcome.Messages);
    }

    // A 400 without messages, from the antiforgery check for instance, tells the user nothing to fix.
    [Theory]
    [InlineData("application/problem+json", "{\"status\":400}")]
    [InlineData("text/html", "<html>Bad Request</html>")]
    public async Task BadRequestWithoutMessages_DidNotGetThrough(string type, string body)
    {
        var material = Material(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body, Encoding.UTF8, type) });

        var outcome = await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    // Content can't get the learning goal from Planning: the text did not get through, the page offers to send it again.
    [Fact]
    public async Task PasteWithoutPlanning_DidNotGetThrough_AndPassesTheTraceOn()
    {
        var material = Material(Problem(HttpStatusCode.ServiceUnavailable, $$"""{"status":503,"traceId":"{{TraceId}}"}"""));

        var outcome = await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken);

        Assert.Equal((CallResult.Failed, TraceId), (outcome.Result, outcome.TraceId));
    }

    // nginx refuses a body above its limit before anything else sees it.
    [Fact]
    public async Task TooLarge_SaysTheLimit()
    {
        var material = Material(new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge)
        {
            Content = new StringContent("<html>413</html>", Encoding.UTF8, "text/html"),
        });

        var outcome = await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.TooLarge, outcome.Result);
        Assert.Equal([HttpLearningMaterial.TextTooLong], outcome.Messages);
    }

    // Content's 429 (#303/#305): its title is the sentence for the user, shown as it is, never with a trace.
    [Fact]
    public async Task Limit_GivesContentsSentence_WithoutTheTrace()
    {
        const string sentence = "Das KI-Kontingent für diesen Monat ist aufgebraucht. Versuch es im nächsten Monat noch einmal.";
        var material = Material(Problem(HttpStatusCode.TooManyRequests, $$"""{"title":"{{sentence}}","status":429,"traceId":"{{TraceId}}"}"""));

        var outcome = await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Limited, outcome.Result);
        Assert.Equal([sentence], outcome.Messages);
        Assert.Null(outcome.TraceId);
    }

    [Fact]
    public async Task Limit_OnUploadAndRetry_IsLimitedToo()
    {
        var material = Material(
            Problem(HttpStatusCode.TooManyRequests, """{"title":"Satz.","status":429}"""),
            Problem(HttpStatusCode.TooManyRequests, """{"title":"Satz.","status":429}"""));

        using var file = new MemoryStream([1, 2, 3]);
        var upload = await material.UploadAsync("a.pdf", file, TestContext.Current.CancellationToken);
        var retry = await material.RetryAsync(Guid.Parse(DocumentId), TestContext.Current.CancellationToken);

        Assert.Equal([CallResult.Limited, CallResult.Limited], [upload.Result, retry.Result]);
        Assert.Equal(["Satz."], upload.Messages);
        Assert.Equal(["Satz."], retry.Messages);
    }

    // A 429 without a sentence of its own, from a proxy or a framework, leaves the words to the page: still a limit, never
    // "not reachable", never a trace.
    [Theory]
    [InlineData("application/problem+json", $$"""{"status":429,"traceId":"{{TraceId}}"}""")]
    [InlineData("application/problem+json", """{"title":"","status":429}""")]
    [InlineData("application/problem+json", """{"title":"   ","status":429}""")]
    [InlineData("application/problem+json", """{"title":"Too Many Requests","status":429}""")]
    [InlineData("application/problem+json", """{"title":"too many requests","status":429}""")]
    [InlineData("application/problem+json", """{"title":"Zeile\neins","status":429}""")]
    [InlineData("application/problem+json", """{"title":42,"status":429}""")]
    [InlineData("application/problem+json", "kein JSON")]
    [InlineData("application/json", """{"title":"Satz.","status":429}""")]
    [InlineData("text/html", "<html>429 Too Many Requests</html>")]
    public async Task LimitWithoutAUsableTitle_IsLimited_WithoutWords(string type, string body)
    {
        var material = Material(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body, Encoding.UTF8, type) });

        var outcome = await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Limited, outcome.Result);
        Assert.Empty(outcome.Messages!);
        Assert.Null(outcome.TraceId);
    }

    [Fact]
    public async Task LimitTitle_UpToTheMost_IsShown_AndLongerIsNot()
    {
        var longest = new string('a', HttpLearningMaterial.LimitTitleMaxLength);
        var material = Material(
            Problem(HttpStatusCode.TooManyRequests, $$"""{"title":"{{longest}}"}"""),
            Problem(HttpStatusCode.TooManyRequests, $$"""{"title":"{{longest}}a"}"""));

        var fits = await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken);
        var tooLong = await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken);

        Assert.Equal([longest], fits.Messages);
        Assert.Equal((CallResult.Limited, 0), (tooLong.Result, tooLong.Messages!.Count));
    }

    [Fact]
    public async Task WithoutALogin_SaysSignedOut()
    {
        var material = Material(Problem(HttpStatusCode.Unauthorized, $$"""{"status":401,"traceId":"{{TraceId}}"}"""));

        Assert.Equal(CallResult.SignedOut, (await material.ListDocumentsAsync(TestContext.Current.CancellationToken)).Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Problem_PassesTheTraceOn(HttpStatusCode code)
    {
        var material = Material(Problem(code, $$"""{"status":500,"traceId":"{{TraceId}}"}"""));

        var outcome = await material.ListTopicsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
        Assert.Equal(TraceId, outcome.TraceId);
    }

    // An answer without what makes a document is no document.
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("""[{"id":"0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01","title":"T","status":"Unknown","createdAt":"2026-11-11T09:00:00Z"}]""")]
    [InlineData("<html></html>")]
    public async Task AnswerWithoutTheDocuments_DidNotGetThrough(string body)
    {
        var material = Material(Json(body));

        Assert.Equal(CallResult.Failed, (await material.ListDocumentsAsync(TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task NoConnection_DidNotGetThrough()
    {
        var material = new HttpLearningMaterial(Client((_, _) => throw new HttpRequestException("Connection refused")));

        Assert.Equal(CallResult.Failed, (await material.PasteAsync(null, "Text", TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task NoAnswerInTime_DidNotGetThrough()
    {
        var material = new HttpLearningMaterial(Client(NeverAsync, timeout: TimeSpan.FromMilliseconds(100)));

        Assert.Equal(CallResult.Failed, (await material.ListDocumentsAsync(TestContext.Current.CancellationToken)).Result);
    }

    // Leaving the page cancels, that is no failure to show.
    [Fact]
    public async Task CancelByTheCaller_IsACancel()
    {
        var material = new HttpLearningMaterial(Client(NeverAsync));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => material.ListDocumentsAsync(cancellation.Token));
    }

    private static async Task<HttpResponseMessage> NeverAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    private static HttpLearningMaterial Material(params HttpResponseMessage[] answers) => Material([], [], answers);

    private static HttpLearningMaterial Material(List<HttpRequestMessage> requests, params HttpResponseMessage[] answers) =>
        Material(requests, [], answers);

    // Answers the requests in the given order and records them with their bodies.
    private static HttpLearningMaterial Material(List<HttpRequestMessage> requests, List<string> bodies, params HttpResponseMessage[] answers)
    {
        var queue = new Queue<HttpResponseMessage>(answers);
        return new HttpLearningMaterial(Client(async (request, cancellationToken) =>
        {
            requests.Add(request);
            if (request.Content is not null)
            {
                bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return queue.Dequeue();
        }));
    }

    private static HttpClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer, TimeSpan? timeout = null) =>
        new(new Handler(answer))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(100),
        };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Created(string body) =>
        new(HttpStatusCode.Created) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}