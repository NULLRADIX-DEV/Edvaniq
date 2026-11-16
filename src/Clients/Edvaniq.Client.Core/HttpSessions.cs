using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Planning through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie.
public sealed class HttpSessions(HttpClient http) : ISessions
{
    public const string Path = "api/planning/sessions";

    public const string BonusPath = "api/planning/sessions/bonus";

    public static string PathOf(Guid sessionId, string action) => $"api/planning/sessions/{sessionId:D}/{action}";

    public Task<CallOutcome<SessionInfo>> StartAsync(Guid blockId, CancellationToken cancellationToken = default) =>
        SendAsync(Path, JsonContent.Create(new StartBody(blockId), SessionRunJson.Default.StartBody), cancellationToken);

    public Task<CallOutcome<SessionInfo>> StartBonusAsync(CancellationToken cancellationToken = default) =>
        SendAsync(BonusPath, null, cancellationToken);

    public Task<CallOutcome<SessionInfo>> AdvanceAsync(Guid sessionId, int step, CancellationToken cancellationToken = default) =>
        SendAsync(PathOf(sessionId, "advance"), JsonContent.Create(new AdvanceBody(step), SessionRunJson.Default.AdvanceBody), cancellationToken);

    public Task<CallOutcome<SessionInfo>> CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        SendAsync(PathOf(sessionId, "complete"), null, cancellationToken);

    public Task<CallOutcome<SessionInfo>> AbortAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        SendAsync(PathOf(sessionId, "abort"), null, cancellationToken);

    private Task<CallOutcome<SessionInfo>> SendAsync(string path, HttpContent? content, CancellationToken cancellationToken) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.PostAsync(path, content, cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<SessionInfo>.SignedOut;
                    case HttpStatusCode.NotFound:
                        return new(CallResult.NotFound, Messages: await TitleAsync(response, cancellationToken));
                    case HttpStatusCode.Conflict:
                        return new(CallResult.Rejected, Messages: await TitleAsync(response, cancellationToken));
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<SessionInfo>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(SessionRunJson.Default.SessionBody, cancellationToken) is { } body
                    && body.ToSession() is { } session
                        ? CallOutcome<SessionInfo>.Ok(session)
                        : CallOutcome<SessionInfo>.Failed();
            },
            cancellationToken);

    private static async Task<IReadOnlyList<string>> TitleAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await ProblemTitle.ReadAsync(response, cancellationToken) is { } title ? [title] : [];
}

internal sealed record StartBody(Guid BlockId);

internal sealed record AdvanceBody(int Step);

// Names come as strings, so a new step or outcome from Planning shows as unknown instead of breaking the page.
internal sealed record SessionBody(
    Guid Id,
    Guid BlockId,
    Guid TopicId,
    IReadOnlyList<StepBody?>? Steps,
    int CurrentStep,
    string? Outcome = null,
    bool Existing = false,
    bool Bonus = false)
{
    // A session without steps, or standing on a step it doesn't have, can't be run.
    public SessionInfo? ToSession() =>
        Steps is { Count: > 0 } steps && steps.All(step => step is not null) && CurrentStep >= 0 && CurrentStep < steps.Count
            ? new(
                Id,
                BlockId,
                TopicId,
                Existing,
                [.. steps.Select(step => step!.ToStep())],
                CurrentStep,
                Outcome switch
                {
                    "running" => SessionOutcome.Running,
                    "aborted" => SessionOutcome.Aborted,
                    "completed" => SessionOutcome.Completed,
                    _ => null,
                },
                Bonus)
            : null;
}

internal sealed record StepBody(string? Kind, bool Empty = false, int? Questions = null)
{
    public SessionStep ToStep() => new(
        Kind switch
        {
            "review" => SessionStepKind.Review,
            "introductionUnit" => SessionStepKind.IntroductionUnit,
            "quiz" => SessionStepKind.Quiz,
            _ => null,
        },
        Empty,
        Questions is > 0 ? Questions : null);
}

// Source-generated for the trimmed browser app. A session without its ids fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SessionBody))]
[JsonSerializable(typeof(StartBody))]
[JsonSerializable(typeof(AdvanceBody))]
internal sealed partial class SessionRunJson : JsonSerializerContext;