using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Planning through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie.
public sealed class HttpSessionResults(HttpClient http, SessionResultPolling? polling = null) : ISessionResults
{
    private readonly SessionResultPolling polling = polling ?? SessionResultPolling.Default;

    public static string PathOf(Guid sessionId) => $"api/planning/sessions/{sessionId}/result";

    public Task<CallOutcome<SessionReport>> GetAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync(PathOf(sessionId), cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<SessionReport>.SignedOut;
                    case HttpStatusCode.NotFound:
                        return new(CallResult.NotFound);
                    case HttpStatusCode.Conflict:
                        return new(CallResult.NotReady);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CallOutcome<SessionReport>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(SessionResultJson.Default.ResultBody, cancellationToken) is { } body
                    ? CallOutcome<SessionReport>.Ok(body.ToReport())
                    : CallOutcome<SessionReport>.Failed();
            },
            cancellationToken);

    public async Task<CallOutcome<SessionReport>> WaitForCompleteAsync(
        Guid sessionId, IProgress<SessionReport>? progress = null, CancellationToken cancellationToken = default)
    {
        var waited = TimeSpan.Zero;
        while (true)
        {
            var outcome = await GetAsync(sessionId, cancellationToken);
            if (outcome is not { Result: CallResult.Ok, Value: { } report })
            {
                return outcome;
            }

            progress?.Report(report);
            if (report.IsSettled || waited >= polling.Limit)
            {
                return outcome;
            }

            await Task.Delay(polling.Interval, cancellationToken);
            waited += polling.Interval;
        }
    }
}

// Names come as strings, so a new one from Planning shows as unknown instead of breaking the page.
internal sealed record ResultBody(
    Guid SessionId,
    string Outcome,
    int Answered,
    int Correct,
    int Wrong,
    bool Complete,
    bool MasteryAvailable,
    IReadOnlyList<TopicBody>? Topics,
    string? DailyGoal,
    NextStepBody? NextStep,
    bool TopicRemoved = false)
{
    public SessionReport ToReport() => new(
        SessionId,
        Outcome == "completed",
        Answered,
        Correct,
        Wrong,
        Complete && !TopicRemoved,
        MasteryAvailable,
        Complete && !TopicRemoved ? Topics?.Select(topic => topic.ToChange()).ToList() : null,
        DailyGoal switch
        {
            "none" => DailyGoalState.None,
            "open" => DailyGoalState.Open,
            "reached" => DailyGoalState.Reached,
            _ => null,
        },
        NextStep is { Kind: "block", BlockId: { } block, TopicId: { } topic, Minutes: { } minutes }
            ? new NextBlock(block, topic, NextStep.BlockKind switch
            {
                "learn" => BlockKind.Learn,
                "review" => BlockKind.Review,
                _ => null,
            }, minutes)
            : null,
        TopicRemoved);
}

internal sealed record TopicBody(Guid TopicId, StandingBody? Before, StandingBody? After)
{
    public MasteryChange ToChange() => new(TopicId, Before?.ToStanding(), After?.ToStanding());
}

internal sealed record StandingBody(int Mastery, string? State)
{
    public MasteryStanding ToStanding() => new(Mastery, State);
}

internal sealed record NextStepBody(string Kind, Guid? BlockId, Guid? TopicId, string? BlockKind, int? Minutes);

// Source-generated for the trimmed browser app.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ResultBody))]
internal sealed partial class SessionResultJson : JsonSerializerContext;