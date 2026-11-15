using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The place of a section in its document through the web host (#350, #351), the link to the file, and the source fields
// the other answers carry.
public sealed class HttpSourcesTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string DocumentId = "1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d";
    private const string SectionId = "4d5e6f7a-8b9c-4d0e-1f2a-3b4c5d6e7f80";

    [Fact]
    public async Task Section_OfAPdf_ReadsItsPages()
    {
        var requests = new List<HttpRequestMessage>();
        var outcome = await Sources(requests, Answer(HttpStatusCode.OK, $$"""
            {"documentId":"{{DocumentId}}","sectionId":"{{SectionId}}","title":"Skript","format":"pdf","hasFile":true,"page":3,"pageTo":4,
             "text":null,"start":null,"length":null,"truncatedBefore":false,"truncatedAfter":false,"position":2}
            """)).SectionAsync(Guid.Parse(SectionId), TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri($"https://edvaniq.test/api/content/sections/{SectionId}/source"), request.RequestUri);
        Assert.Equal(
            new SectionSource(Guid.Parse(DocumentId), Guid.Parse(SectionId), "Skript", SourceFormats.Pdf, true, 3, 4, Position: 2),
            outcome.Value);
    }

    // A pasted text has its window and no format. Without position (an older Content) it's still read.
    [Fact]
    public async Task Section_OfAPastedText_ReadsItsWindow()
    {
        var outcome = await Sources([], Answer(HttpStatusCode.OK, $$"""
            {"documentId":"{{DocumentId}}","sectionId":"{{SectionId}}","title":"Mitschrift","format":null,"hasFile":false,
             "text":"Davor. Das. Danach.","start":7,"length":4,"truncatedBefore":true,"truncatedAfter":false}
            """)).SectionAsync(Guid.Parse(SectionId), TestContext.Current.CancellationToken);

        var source = outcome.Value!;
        Assert.Equal((null, "Davor. Das. Danach.", 7, 4, true, false, (int?)null),
            (source.Format, source.Text, source.Start, source.Length, source.TruncatedBefore, source.TruncatedAfter, source.Position));
    }

    // A mark outside its text would cut the text wrong.
    [Theory]
    [InlineData("\"text\":\"kurz\",\"start\":2,\"length\":5")]
    [InlineData("\"text\":\"kurz\",\"start\":-1,\"length\":1")]
    [InlineData("\"text\":\"kurz\",\"start\":null,\"length\":1")]
    [InlineData("\"text\":null,\"start\":0,\"length\":1")]
    public async Task MarkOutsideItsText_Fails(string window)
    {
        var outcome = await Sources([], Answer(HttpStatusCode.OK, $$"""
            {"documentId":"{{DocumentId}}","sectionId":"{{SectionId}}","title":"T","format":"text","hasFile":true,{{window}},
             "truncatedBefore":false,"truncatedAfter":false}
            """)).SectionAsync(Guid.Parse(SectionId), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task Gone_IsNotFound()
    {
        var outcome = await Sources([], Problem(HttpStatusCode.NotFound)).SectionAsync(Guid.Parse(SectionId), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.NotFound, outcome.Result);
    }

    [Fact]
    public async Task Unauthorized_IsSignedOut()
    {
        var outcome = await Sources([], Problem(HttpStatusCode.Unauthorized)).SectionAsync(Guid.Parse(SectionId), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.SignedOut, outcome.Result);
    }

    [Fact]
    public async Task ServerError_FailsWithTheTrace()
    {
        var outcome = await Sources([], Problem(HttpStatusCode.BadGateway)).SectionAsync(Guid.Parse(SectionId), TestContext.Current.CancellationToken);

        Assert.Equal((CallResult.Failed, TraceId), (outcome.Result, outcome.TraceId));
    }

    [Theory]
    [InlineData("""{"sectionId":"4d5e6f7a-8b9c-4d0e-1f2a-3b4c5d6e7f80","title":"T","format":null,"hasFile":false}""")]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("null")]
    public async Task AnswerThatIsNoSource_Fails(string body)
    {
        var outcome = await Sources([], Answer(HttpStatusCode.OK, body)).SectionAsync(Guid.Parse(SectionId), TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    // Relative to the app's base address, so the web host's cookie goes with it. The page number never takes the
    // browser's culture.
    [Fact]
    public void FilePath_IsRelative_AndNamesThePage()
    {
        var document = Guid.Parse(DocumentId);

        Assert.Equal($"api/content/documents/{DocumentId}/file", HttpSources.FilePath(document));
        Assert.Equal($"api/content/documents/{DocumentId}/file#page=1234", HttpSources.FilePath(document, 1234));
    }

    // Assessment names the source of each question (#349).
    [Fact]
    public async Task Question_CarriesItsSource()
    {
        var quiz = new HttpQuiz(Client(_ => Answer(HttpStatusCode.Created, $$"""
            {"id":"{{SectionId}}","topicId":"{{DocumentId}}","questionId":"{{SectionId}}","type":"singleChoice","text":"Wo?",
             "options":["A","B"],"difficulty":1,"source":{"documentId":"{{DocumentId}}","sectionId":"{{SectionId}}","model":"m","promptVersion":"questions-3"} }
            """)));

        var outcome = await quiz.AskAsync(Guid.Parse(DocumentId), TestContext.Current.CancellationToken);

        Assert.Equal(new QuestionSource(Guid.Parse(DocumentId), Guid.Parse(SectionId), "m", "questions-3"), outcome.Value!.Source);
    }

    // Content names the origin of each document (#349, #411), unknown values come as they are.
    [Theory]
    [InlineData("material")]
    [InlineData("synthetic")]
    [InlineData("demo")]
    [InlineData("borrowed")]
    public async Task Document_CarriesItsOrigin(string origin)
    {
        var material = new HttpLearningMaterial(Client(_ => Answer(HttpStatusCode.OK, $$"""
            [{"id":"{{DocumentId}}","title":"Skript","status":"Ready","createdAt":"2026-11-15T09:00:00Z","source":"file","origin":"{{origin}}"}]
            """)));

        var outcome = await material.ListDocumentsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(origin, Assert.Single(outcome.Value!).Origin);
        Assert.Equal(origin == "synthetic", DocumentOrigins.IsSynthetic(origin));
    }

    // A summary names origin and first section of each source (#349).
    [Fact]
    public async Task SummarySource_CarriesOriginAndSection()
    {
        var summaries = new HttpTopicSummaries(Client(_ => Answer(HttpStatusCode.OK, $$"""
            {"topicId":"{{DocumentId}}","length":"standard","status":"ready","text":"T","aiGenerated":true,
             "sources":[{"documentId":"{{DocumentId}}","documentTitle":"Skript","pageFrom":3,"pageTo":4,"origin":"demo","sectionId":"{{SectionId}}"},
                        {"documentId":"{{SectionId}}","documentTitle":"Alt","pageFrom":null,"pageTo":null}]}
            """)));

        var outcome = await summaries.GetAsync(Guid.Parse(DocumentId), SummaryLength.Standard, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new SummarySource(Guid.Parse(DocumentId), "Skript", 3, 4, "demo", Guid.Parse(SectionId)),
                new SummarySource(Guid.Parse(SectionId), "Alt", null, null),
            ],
            outcome.Value!.Sources);
    }

    private static HttpSources Sources(List<HttpRequestMessage> requests, HttpResponseMessage answer) =>
        new(Client(request =>
        {
            requests.Add(request);
            return answer;
        }));

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(new Handler(answer)) { BaseAddress = new Uri("https://edvaniq.test/") };

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