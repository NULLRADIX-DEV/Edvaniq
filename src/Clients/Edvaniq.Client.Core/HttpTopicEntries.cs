using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Knowledge through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie. BusyPause is how long it waits before it tries a change once more that came back busy.
public sealed class HttpTopicEntries(HttpClient http, TimeSpan? busyPause = null) : ITopicEntries
{
    public const string Path = "api/knowledge/topics";

    private readonly TimeSpan pause = busyPause ?? TimeSpan.FromMilliseconds(300);

    public Task<CallOutcome<TopicAdded>> AddAsync(Guid learningGoalId, string name, CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.PostAsJsonAsync(
                    Path, new NewTopic(name, learningGoalId), TopicJson.Default.NewTopic, cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Created:
                        return CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: false));
                    case HttpStatusCode.Conflict:
                        return CallOutcome<TopicAdded>.Ok(new TopicAdded(AlreadyThere: true));
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<TopicAdded>.SignedOut;
                    case HttpStatusCode.BadRequest:
                        var (error, traceId) = await NameErrorAsync(response, cancellationToken);
                        return error is null
                            ? CallOutcome<TopicAdded>.Failed(traceId)
                            : new CallOutcome<TopicAdded>(CallResult.Rejected, Messages: [error]);
                    default:
                        return CallOutcome<TopicAdded>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }
            },
            cancellationToken);

    public Task<CallOutcome<TopicEdit>> CreateTopicAsync(Guid learningGoalId, string name, CancellationToken cancellationToken = default) =>
        EditAsync(() => http.PostAsJsonAsync(Path, new NewTopicIn(name, learningGoalId, null), TopicJson.Default.NewTopicIn, cancellationToken), cancellationToken);

    public Task<CallOutcome<TopicEdit>> CreateSubtopicAsync(Guid parentId, string name, CancellationToken cancellationToken = default) =>
        EditAsync(() => http.PostAsJsonAsync(Path, new NewTopicIn(name, null, parentId), TopicJson.Default.NewTopicIn, cancellationToken), cancellationToken);

    public Task<CallOutcome<TopicEdit>> RenameAsync(Guid topicId, string name, CancellationToken cancellationToken = default) =>
        EditAsync(() => http.PatchAsJsonAsync($"{Path}/{topicId}", new RenamedTopic(name), TopicJson.Default.RenamedTopic, cancellationToken), cancellationToken);

    public Task<CallOutcome<TopicEdit>> HideAsync(Guid topicId, CancellationToken cancellationToken = default) =>
        EditAsync(() => http.PutAsync($"{Path}/{topicId}/hidden", content: null, cancellationToken), cancellationToken);

    public Task<CallOutcome<TopicEdit>> ShowAsync(Guid topicId, CancellationToken cancellationToken = default) =>
        EditAsync(() => http.DeleteAsync($"{Path}/{topicId}/hidden", cancellationToken), cancellationToken);

    // A busy answer gets one more try after a short pause, not more: the user can try again.
    private async Task<CallOutcome<TopicEdit>> EditAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        var outcome = await HostCall.GuardAsync(() => SendEditAsync(send, cancellationToken), cancellationToken);
        if (outcome.Value?.Conflict != TopicConflict.Busy)
        {
            return outcome;
        }

        await Task.Delay(pause, cancellationToken);
        return await HostCall.GuardAsync(() => SendEditAsync(send, cancellationToken), cancellationToken);
    }

    private static async Task<CallOutcome<TopicEdit>> SendEditAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        using var response = await send();
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK or HttpStatusCode.Created:
                return await response.Content.ReadFromJsonAsync(TopicJson.Default.EditedTopic, cancellationToken) is { } topic
                    ? CallOutcome<TopicEdit>.Ok(new TopicEdit(topic))
                    : CallOutcome<TopicEdit>.Failed();
            case HttpStatusCode.Unauthorized:
                return CallOutcome<TopicEdit>.SignedOut;
            case HttpStatusCode.NotFound:
                return new CallOutcome<TopicEdit>(CallResult.NotFound);
            case HttpStatusCode.Conflict:
                var (conflict, conflictTrace) = await ConflictAsync(response, cancellationToken);
                return new CallOutcome<TopicEdit>(CallResult.Rejected, new TopicEdit(Conflict: conflict), conflictTrace);
            case HttpStatusCode.BadRequest:
                var (error, traceId) = await NameErrorAsync(response, cancellationToken);
                return error is null
                    ? CallOutcome<TopicEdit>.Failed(traceId)
                    : new CallOutcome<TopicEdit>(CallResult.Rejected, new TopicEdit(), traceId, FieldErrors: new Dictionary<string, string> { ["Name"] = error });
            default:
                return CallOutcome<TopicEdit>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
        }
    }

    // Only the code tells the conflicts apart, never Knowledge's text. One without a known code is Other.
    private static async Task<(TopicConflict Conflict, string? TraceId)> ConflictAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return (TopicConflict.Other, null);
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync(TopicJson.Default.ConflictProblem, cancellationToken);
            var conflict = problem?.Code switch
            {
                "nameTaken" => TopicConflict.NameTaken,
                "parentHidden" => TopicConflict.ParentHidden,
                "busy" => TopicConflict.Busy,
                _ => TopicConflict.Other,
            };
            return (conflict, ProblemTrace.Check(problem?.TraceId));
        }
        catch (JsonException)
        {
            return (TopicConflict.Other, null);
        }
    }

    // What Knowledge says about the name. Any other refusal is no fault of the user's input. The body can be read only
    // once.
    private static async Task<(string? Error, string? TraceId)> NameErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync(TopicJson.Default.FieldProblem, cancellationToken);
            var error = problem?.Errors?
                .Where(entry => string.Equals(entry.Key, "Name", StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Value ?? [])
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            return (error, ProblemTrace.Check(problem?.TraceId));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}

internal sealed record NewTopic(string Name, Guid LearningGoalId);

// Exactly one of the two, the other stays out of the JSON.
internal sealed record NewTopicIn(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? LearningGoalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ParentId);

internal sealed record RenamedTopic(string Name);

internal sealed record ConflictProblem(string? Code = null, string? TraceId = null);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(NewTopic))]
[JsonSerializable(typeof(NewTopicIn))]
[JsonSerializable(typeof(RenamedTopic))]
[JsonSerializable(typeof(EditedTopic))]
[JsonSerializable(typeof(ConflictProblem))]
[JsonSerializable(typeof(FieldProblem))]
internal sealed partial class TopicJson : JsonSerializerContext;