using System.Net;
using System.Text;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// The change of the password through the web host: what goes out, how Identity's refusals come back.
public sealed class HttpPasswordChangeTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task Change_PostsBothPasswords_AndNoContentIsOk()
    {
        var requests = new List<HttpRequestMessage>();
        var bodies = new List<string>();
        var passwords = Passwords(requests, bodies, new HttpResponseMessage(HttpStatusCode.NoContent));

        var outcome = await passwords.ChangeAsync("old one", "new one", TestContext.Current.CancellationToken);

        var request = Assert.Single(requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://edvaniq.test/api/identity/accounts/password"), request.RequestUri);
        Assert.Equal("""{"currentPassword":"old one","newPassword":"new one"}""", Assert.Single(bodies));
        Assert.Equal(CallResult.Ok, outcome.Result);
    }

    [Theory]
    [InlineData("current_password_invalid", "Dein aktuelles Passwort stimmt nicht.", PasswordChangeFields.Current)]
    [InlineData("password_rules", "Dein Passwort braucht mindestens 12 Zeichen.", PasswordChangeFields.New)]
    public async Task Refusal_IsTheSentence_AtItsField(string code, string title, string field)
    {
        var passwords = Passwords([], [], Problem(HttpStatusCode.BadRequest, $$"""{"status":400,"title":"{{title}}","code":"{{code}}","traceId":"{{TraceId}}"}"""));

        var outcome = await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal(new Dictionary<string, string> { [field] = title }, outcome.FieldErrors);
    }

    // One of the two refusals whose title is no sentence for the user still belongs at its field, with our own sentence.
    [Theory]
    [InlineData("current_password_invalid", 0, PasswordChangeFields.Current, "Dein aktuelles Passwort stimmt nicht.")]
    [InlineData("password_rules", 301, PasswordChangeFields.New, "Dein neues Passwort braucht mindestens 12 und höchstens 256 Zeichen.")]
    public async Task Refusal_WithoutASentence_IsOurSentence_AtItsField(string code, int titleLength, string field, string sentence)
    {
        var title = new string('x', titleLength);
        var passwords = Passwords([], [], Problem(HttpStatusCode.BadRequest, $$"""{"status":400,"title":"{{title}}","code":"{{code}}","traceId":"{{TraceId}}"}"""));

        var outcome = await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Rejected, outcome.Result);
        Assert.Equal(new Dictionary<string, string> { [field] = sentence }, outcome.FieldErrors);
    }

    // A 400 that is none of the two refusals is no message for a field.
    [Theory]
    [InlineData("""{"status":400,"title":"Bad Request","code":"something_else","traceId":"4bf92f3577b34da6a3ce929d0e0e4736"}""")]
    [InlineData("""{"status":400,"title":"Bad Request","traceId":"4bf92f3577b34da6a3ce929d0e0e4736"}""")]
    public async Task OtherBadRequest_DidNotGetThrough_WithTheTrace(string body)
    {
        var passwords = Passwords([], [], Problem(HttpStatusCode.BadRequest, body));

        var outcome = await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken);

        Assert.Equal((CallResult.Failed, TraceId), (outcome.Result, outcome.TraceId));
        Assert.Null(outcome.FieldErrors);
    }

    [Fact]
    public async Task TooManyRequests_IsLimited_WithTheSentence()
    {
        var passwords = Passwords([], [], Problem(HttpStatusCode.TooManyRequests, """{"status":429,"title":"Zu viele falsche Passwörter. Warte ein paar Minuten.","code":"locked"}"""));

        var outcome = await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Limited, outcome.Result);
        Assert.Equal(["Zu viele falsche Passwörter. Warte ein paar Minuten."], outcome.Messages);
    }

    [Fact]
    public async Task TooManyRequests_WithoutASentence_IsLimitedWithoutMessage()
    {
        var passwords = Passwords([], [], new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var outcome = await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken);

        Assert.Equal(CallResult.Limited, outcome.Result);
        Assert.Empty(outcome.Messages!);
    }

    [Fact]
    public async Task WithoutALogin_SaysSignedOut()
    {
        var passwords = Passwords([], [], Problem(HttpStatusCode.Unauthorized, """{"status":401}"""));

        Assert.Equal(CallResult.SignedOut, (await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task AccountThatIsGone_IsNotFound()
    {
        var passwords = Passwords([], [], Problem(HttpStatusCode.NotFound, """{"status":404,"title":"Dieses Konto gibt es nicht mehr.","code":"account_not_found"}"""));

        Assert.Equal(CallResult.NotFound, (await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task ServerProblem_PassesTheTraceOn()
    {
        var passwords = Passwords([], [], Problem(HttpStatusCode.ServiceUnavailable, $$"""{"status":503,"traceId":"{{TraceId}}"}"""));

        var outcome = await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken);

        Assert.Equal((CallResult.Failed, TraceId), (outcome.Result, outcome.TraceId));
    }

    [Fact]
    public async Task AnswerThatIsNoProblem_DidNotGetThrough()
    {
        var html = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") };
        var passwords = Passwords([], [], html);

        Assert.Equal(CallResult.Failed, (await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task NoConnection_DidNotGetThrough()
    {
        var passwords = new HttpPasswordChange(new HttpClient(new Handler((_, _) => throw new HttpRequestException("Connection refused")))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
        });

        Assert.Equal(CallResult.Failed, (await passwords.ChangeAsync("a", "b", TestContext.Current.CancellationToken)).Result);
    }

    private static HttpPasswordChange Passwords(List<HttpRequestMessage> requests, List<string> bodies, params HttpResponseMessage[] answers)
    {
        var queue = new Queue<HttpResponseMessage>(answers);
        return new HttpPasswordChange(new HttpClient(new Handler(async (request, cancellationToken) =>
        {
            requests.Add(request);
            if (request.Content is not null)
            {
                bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return queue.Dequeue();
        })) { BaseAddress = new Uri("https://edvaniq.test/") });
    }

    private static HttpResponseMessage Problem(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}
