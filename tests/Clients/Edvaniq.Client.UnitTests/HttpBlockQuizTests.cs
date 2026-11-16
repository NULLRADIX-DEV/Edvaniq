using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The quiz of a block through the web host (#357): what goes out, how the questions and errors come back.
public sealed class HttpBlockQuizTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string QuizId = "1d2e3f4a-5b6c-4d7e-8f9a-0b1c2d3e4f51";
    private const string BlockId = "2e3f4a5b-6c7d-4e8f-9a0b-1c2d3e4f5a62";
    private const string TopicId = "5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50";
    private const string FirstAttempt = "0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01";
    private const string SecondAttempt = "7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e02";
    private const string DocumentId = "8d2e3f4a-5b6c-4d7e-8f9a-0b1c2d3e4f03";
    private const string SectionId = "9e3f4a5b-6c7d-4e8f-9a0b-1c2d3e4f5a04";

    // As Assessment's QuizResponse names the fields: the first question answered right, the second still open.
    private const string QuizJson =
        $$$"""
        {"id":"{{{QuizId}}}","blockId":"{{{BlockId}}}","topicId":"{{{TopicId}}}","poolTooSmall":false,"questions":[
         {"id":"{{{FirstAttempt}}}","topicId":"{{{TopicId}}}","questionId":"{{{FirstAttempt}}}","type":"trueFalse","text":"Wahr?",
          "options":["Wahr","Falsch"],"difficulty":1,"answered":true,"correct":true,
          "source":{"documentId":"{{{DocumentId}}}","sectionId":"{{{SectionId}}}","model":"m","promptVersion":"questions-2"}},
         {"id":"{{{SecondAttempt}}}","topicId":"{{{TopicId}}}","questionId":"{{{SecondAttempt}}}","type":"multipleChoice","text":"Welche?",
          "options":["A","B","C"],"difficulty":2,"answered":false,"correct":null,
          "source":{"documentId":"{{{DocumentId}}}","sectionId":"{{{SectionId}}}","model":"m","promptVersion":"questions-2"}}]}
        """;

    [Fact]
    public async Task Open_PostsTopicBlockAndSize_AndReadsTheQuestions()
    {
        var requests = new List<(HttpRequestMessage Request, string? Body)>();
        var quizzes = Quizzes(requests, Answer(HttpStatusCode.OK, QuizJson));

        var outcome = await quizzes.OpenAsync(Guid.Parse(TopicId), Guid.Parse(BlockId), 10, TestContext.Current.CancellationToken);

        var (request, body) = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/assessment/quizzes"), request.RequestUri);
        Assert.Equal($$"""{"topicId":"{{TopicId}}","blockId":"{{BlockId}}","size":10}""", body);
        Assert.Equal(CallResult.Ok, outcome.Result);
        var quiz = outcome.Value!;
        Assert.Equal(Guid.Parse(QuizId), quiz.Id);
        Assert.False(quiz.PoolTooSmall);
        Assert.Collection(
            quiz.Questions,
            first =>
            {
                Assert.True(first.Answered);
                Assert.True(first.Correct);
                Assert.Equal(QuestionTypes.TrueFalse, first.Type);
            },
            second =>
            {
                Assert.False(second.Answered);
                Assert.Null(second.Correct);
                Assert.Equal(new AskedQuestion(
                    Guid.Parse(SecondAttempt), Guid.Parse(TopicId), Guid.Parse(SecondAttempt), "Welche?", second.Options, 2,
                    QuestionTypes.MultipleChoice, new QuestionSource(Guid.Parse(DocumentId), Guid.Parse(SectionId), "m", "questions-2")),
                    second.Asked);
                Assert.True(second.Asked.IsMultipleChoice);
            });
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CallResult.SignedOut)]
    [InlineData(HttpStatusCode.NotFound, CallResult.NotFound)]
    [InlineData(HttpStatusCode.Conflict, CallResult.NotReady)]
    [InlineData(HttpStatusCode.BadGateway, CallResult.Failed)]
    public async Task Errors_AreNamed(HttpStatusCode status, CallResult expected)
    {
        var quizzes = Quizzes([], Problem(status));

        var outcome = await quizzes.OpenAsync(Guid.Parse(TopicId), Guid.Parse(BlockId), 10, TestContext.Current.CancellationToken);

        Assert.Equal(expected, outcome.Result);
        Assert.Null(outcome.Value);
    }

    // A quiz without questions, or one the page couldn't show, didn't really get through.
    [Theory]
    [InlineData("\"questions\":[]")]
    [InlineData("\"options\":[\"A\"]")]
    public async Task UnusableQuiz_Fails(string broken)
    {
        var body = broken.StartsWith("\"questions\"", StringComparison.Ordinal)
            ? $$"""{"id":"{{QuizId}}","blockId":"{{BlockId}}","topicId":"{{TopicId}}","poolTooSmall":false,{{broken}}}"""
            : QuizJson.Replace("\"options\":[\"A\",\"B\",\"C\"]", broken, StringComparison.Ordinal);
        var quizzes = Quizzes([], Answer(HttpStatusCode.OK, body));

        var outcome = await quizzes.OpenAsync(Guid.Parse(TopicId), Guid.Parse(BlockId), 10, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    [Fact]
    public async Task NoConnection_Fails()
    {
        var quizzes = new HttpQuiz(Client((_, _) => throw new HttpRequestException("down")));

        var outcome = await quizzes.OpenAsync(Guid.Parse(TopicId), Guid.Parse(BlockId), 10, TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Failed, outcome.Result);
    }

    private static IBlockQuiz Quizzes(List<(HttpRequestMessage Request, string? Body)> requests, params HttpResponseMessage[] answers)
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