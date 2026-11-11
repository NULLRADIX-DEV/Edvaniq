using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Edvaniq.Client.Core;

// Talks to Assessment through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie, its timeout is longer than the host's own wait for the gateway.
public sealed class HttpQuiz(HttpClient http) : IQuiz
{
    public const string AttemptsPath = "api/assessment/attempts";

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

    // A question the page couldn't show as single choice, or a grade pointing at no option, didn't really get through.
    // Only a shown solution has no option.
    private static bool Valid<T>(T value) => value switch
    {
        AskedQuestion question => question.Options.Count >= 2,
        GradedAnswer graded => (graded.Option is { } option ? option >= 0 : graded.Revealed) && graded.Answer >= 0,
        _ => true,
    };
}

internal sealed record AskBody(Guid TopicId);

internal sealed record AnswerBody(int Option, int ResponseTimeMs);

internal sealed record RevealBody(bool ShowSolution, int ResponseTimeMs);

// Source-generated for the trimmed browser app. An answer without the fields that make a question or a grade fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AskBody))]
[JsonSerializable(typeof(AnswerBody))]
[JsonSerializable(typeof(RevealBody))]
[JsonSerializable(typeof(AskedQuestion))]
[JsonSerializable(typeof(GradedAnswer))]
internal sealed partial class QuizJson : JsonSerializerContext;