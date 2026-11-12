using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// #294: what the topic directory asks the web host and how it reads the answers.
public sealed class HttpTopicDirectoryTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Document = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");
    private static readonly Guid Section = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    [Fact]
    public async Task Topics_AreAskedWithTheHiddenOnes_AndKeepTheMark()
    {
        var requests = new List<HttpRequestMessage>();
        var directory = Directory(requests, Json($$"""
            [{"id":"{{Topic}}","name":"Photosynthese","documentId":null,"position":0,"hidden":true,
              "subtopics":[{"id":"{{Section}}","name":"Lichtreaktion","position":1,"hidden":true}]}]
            """));

        var outcome = await directory.ListTopicsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://edvaniq.test/api/knowledge/topics?includeHidden=true"), Assert.Single(requests).RequestUri);
        var topic = Assert.Single(outcome.Value!);
        Assert.True(topic.Hidden);
        Assert.True(Assert.Single(topic.Subtopics).Hidden);
    }

    [Fact]
    public async Task Terms_AreAskedFromKnowledge()
    {
        var requests = new List<HttpRequestMessage>();
        var directory = Directory(requests, Json($$"""
            [{"term":"Chlorophyll","definition":"Grüner Farbstoff.","documentId":"{{Document}}","sectionId":"{{Section}}"}]
            """));

        var outcome = await directory.TermsAsync(Topic, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri($"https://edvaniq.test/api/knowledge/topics/{Topic}/terms"), Assert.Single(requests).RequestUri);
        Assert.Equal(new KeyTermInfo("Chlorophyll", "Grüner Farbstoff.", Document, Section), Assert.Single(outcome.Value!));
    }

    [Fact]
    public async Task SectionsOfATopic_AreAskedFromKnowledge()
    {
        var requests = new List<HttpRequestMessage>();
        var directory = Directory(requests, Json($$"""[{"documentId":"{{Document}}","sectionId":"{{Section}}"}]"""));

        var outcome = await directory.TopicSectionsAsync(Topic, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri($"https://edvaniq.test/api/knowledge/topics/{Topic}/sections"), Assert.Single(requests).RequestUri);
        Assert.Equal(new TopicSectionInfo(Document, Section), Assert.Single(outcome.Value!));
    }

    // Pages only for a file, a pasted text has none.
    [Fact]
    public async Task SectionsOfADocument_AreAskedFromContent()
    {
        var requests = new List<HttpRequestMessage>();
        var directory = Directory(requests, Json($$"""
            [{"sectionId":"{{Section}}","position":0,"pageFrom":3,"pageTo":4},{"sectionId":"{{Topic}}","position":1,"pageFrom":null,"pageTo":null}]
            """));

        var outcome = await directory.DocumentSectionsAsync(Document, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri($"https://edvaniq.test/api/content/documents/{Document}/sections"), Assert.Single(requests).RequestUri);
        Assert.Equal([new DocumentSectionInfo(Section, 0, 3, 4), new DocumentSectionInfo(Topic, 1)], outcome.Value!);
    }

    [Fact]
    public async Task Gone_IsNotFound_AndSignedOutAndFailedAsEverywhere()
    {
        var directory = Directory([],
            Problem(HttpStatusCode.NotFound),
            Problem(HttpStatusCode.Unauthorized),
            Problem(HttpStatusCode.BadGateway));

        var gone = await directory.DocumentSectionsAsync(Document, TestContext.Current.CancellationToken);
        var signedOut = await directory.TermsAsync(Topic, TestContext.Current.CancellationToken);
        var failed = await directory.TopicSectionsAsync(Topic, TestContext.Current.CancellationToken);

        Assert.Equal([CallResult.NotFound, CallResult.SignedOut, CallResult.Failed], [gone.Result, signedOut.Result, failed.Result]);
        Assert.Equal(TraceId, failed.TraceId);
    }

    [Theory]
    [InlineData("[{}]")]
    [InlineData("""[{"term":"Chlorophyll"}]""")]
    [InlineData("<html></html>")]
    public async Task TermWithoutItsParts_DidNotGetThrough(string body)
    {
        var directory = Directory([], Json(body));

        Assert.Equal(CallResult.Failed, (await directory.TermsAsync(Topic, TestContext.Current.CancellationToken)).Result);
    }

    private static HttpTopicDirectory Directory(List<HttpRequestMessage> requests, params HttpResponseMessage[] answers)
    {
        var queue = new Queue<HttpResponseMessage>(answers);
        return new HttpTopicDirectory(new HttpClient(new Handler(request =>
        {
            requests.Add(request);
            return queue.Dequeue();
        }))
        { BaseAddress = new Uri("https://edvaniq.test/") });
    }

    private static HttpResponseMessage Json(string body) =>
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