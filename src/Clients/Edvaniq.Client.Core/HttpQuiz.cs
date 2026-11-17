using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Edvaniq.Client.Core;

// Talks to Assessment through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie, its timeout is longer than the host's own wait for the gateway.
public sealed class HttpQuiz(HttpClient http) : IQuiz, IBlockQuiz, ICheckQuestions
{
    public const string AttemptsPath = "api/assessment/attempts";

    public const string QuizzesPath = "api/assessment/quizzes";

    public const string CheckQuestionsPath = "api/assessment/check-questions";

    // Assessment's two words. Any other status, or "ready" without questions, didn't really get through.
    private const string Ready = "ready";
    private const string NotReady = "notReady";

    public Task<CallOutcome<BlockQuiz>> OpenAsync(Guid topicId, Guid blockId, int size, CancellationToken cancellationToken = default) =>
        CallAsync(
            token => http.PostAsync(QuizzesPath, JsonContent.Create(new QuizBody(topicId, blockId, size), QuizJson.Default.QuizBody), token),
            QuizJson.Default.BlockQuiz,
            cancellationToken);

    public async Task<CallOutcome<CheckQuestions>> StartAsync(Guid topicId, Guid? sessionId, CancellationToken cancellationToken = default)
    {
        var outcome = await CallAsync(
            token => http.PostAsync(
                CheckQuestionsPath, JsonContent.Create(new CheckBody(topicId, sessionId), QuizJson.Default.CheckBody), token),
            QuizJson.Default.CheckResponse,
            cancellationToken);
        return outcome is { Result: CallResult.Ok, Value: { } check }
            ? CallOutcome<CheckQuestions>.Ok(new CheckQuestions(check.TopicId, check.Status == Ready, check.Status == Ready ? check.Questions : []))
            : new CallOutcome<CheckQuestions>(outcome.Result, TraceId: outcome.TraceId);
    }

    public Task<CallOutcome<AskedQuestion>> AskAsync(Guid topicId, CancellationToken cancellationToken = default) =>
        CallAsync(
            token => http.PostAsync(AttemptsPath, JsonContent.Create(new AskBody(topicId), QuizJson.Default.AskBody), token),
            QuizJson.Default.AskedQuestion,
            cancellationToken);

    public Task<CallOutcome<GradedAnswer>> AnswerAsync(Guid attemptId, int option, int responseTimeMs, CancellationToken cancellationToken = default) =>
        CallAsync(
            token => http.PostAsync(
                $"{AttemptsPath}/{attemptId:D}/answer",
                JsonContent.Create(new AnswerBody(option, responseTimeMs), QuizJson.Default.AnswerBody),
                token),
            QuizJson.Default.GradedAnswer,
            cancellationToken);

    public Task<CallOutcome<GradedAnswer>> AnswerAsync(Guid attemptId, IReadOnlyList<int> options, int responseTimeMs, CancellationToken cancellationToken = default) =>
        CallAsync(
            token => http.PostAsync(
                $"{AttemptsPath}/{attemptId:D}/answer",
                JsonContent.Create(new ChoicesBody([.. options.Distinct().Order()], responseTimeMs), QuizJson.Default.ChoicesBody),
                token),
            QuizJson.Default.GradedAnswer,
            cancellationToken);

    // Same path as an answer, the host needs no route of its own.
    public Task<CallOutcome<GradedAnswer>> RevealAsync(Guid attemptId, int responseTimeMs, CancellationToken cancellationToken = default) =>
        CallAsync(
            token => http.PostAsync(
                $"{AttemptsPath}/{attemptId:D}/answer",
                JsonContent.Create(new RevealBody(true, responseTimeMs), QuizJson.Default.RevealBody),
                token),
            QuizJson.Default.GradedAnswer,
            cancellationToken);

    private static Task<CallOutcome<T>> CallAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send, JsonTypeInfo<T> answer, CancellationToken cancellationToken) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await send(cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<T>.SignedOut;
                    case HttpStatusCode.NotFound:
                        return new CallOutcome<T>(CallResult.NotFound);
                    case HttpStatusCode.Conflict:
                        return new CallOutcome<T>(CallResult.NotReady);
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<T>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(answer, cancellationToken) is { } value && Valid(value)
                    ? CallOutcome<T>.Ok(value)
                    : CallOutcome<T>.Failed();
            },
            cancellationToken);

    // A question the page couldn't show (no text, fewer than two options, an empty one), or a grade pointing at no option,
    // didn't really get through. Only a shown solution has no option.
    private static bool Valid<T>(T value) => value switch
    {
        AskedQuestion question => Shows(question.Text, question.Options),
        BlockQuiz quiz => quiz.Questions is { Count: > 0 } questions && questions.All(question => question is not null && Shows(question.Text, question.Options)),
        CheckResponse check => check.Status switch
        {
            Ready => check.Questions is { Count: > 0 } questions && questions.All(question => question is not null && Shows(question.Text, question.Options)),
            NotReady => true,
            _ => false,
        },
        GradedAnswer graded => (graded.Option is { } option ? option >= 0
                : graded.Options is { Count: > 0 } options ? options.All(chosen => chosen >= 0)
                : graded.Revealed)
            && graded.Answer >= 0
            && graded.RightOptions.All(right => right >= 0),
        _ => true,
    };

    private static bool Shows(string? text, IReadOnlyList<string?>? options) =>
        !string.IsNullOrWhiteSpace(text) && options is { Count: >= 2 } && options.All(option => !string.IsNullOrWhiteSpace(option));
}

internal sealed record AskBody(Guid TopicId);

internal sealed record AnswerBody(int Option, int ResponseTimeMs);

internal sealed record ChoicesBody(IReadOnlyList<int> Options, int ResponseTimeMs);

internal sealed record RevealBody(bool ShowSolution, int ResponseTimeMs);

internal sealed record QuizBody(Guid TopicId, Guid BlockId, int Size);

internal sealed record CheckBody(Guid TopicId, Guid? SessionId);

internal sealed record CheckResponse(Guid TopicId, string Status, IReadOnlyList<AskedQuestion> Questions);

// Source-generated for the trimmed browser app. An answer without the fields that make a question or a grade fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AskBody))]
[JsonSerializable(typeof(AnswerBody))]
[JsonSerializable(typeof(ChoicesBody))]
[JsonSerializable(typeof(RevealBody))]
[JsonSerializable(typeof(AskedQuestion))]
[JsonSerializable(typeof(GradedAnswer))]
[JsonSerializable(typeof(QuizBody))]
[JsonSerializable(typeof(BlockQuiz))]
[JsonSerializable(typeof(CheckBody))]
[JsonSerializable(typeof(CheckResponse))]
internal sealed partial class QuizJson : JsonSerializerContext;