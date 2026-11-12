using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Knowledge through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie.
public sealed class HttpTopicEntries(HttpClient http) : ITopicEntries
{
    public const string Path = "api/knowledge/topics";

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

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(NewTopic))]
[JsonSerializable(typeof(FieldProblem))]
internal sealed partial class TopicJson : JsonSerializerContext;