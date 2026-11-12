using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Planning through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie.
public sealed class HttpLearningGoals(HttpClient http) : ILearningGoals
{
    public const string Path = "api/planning/learning-goals";

    public Task<CallOutcome<IReadOnlyList<LearningGoalInfo>>> ListAsync(CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync(Path, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return CallOutcome<IReadOnlyList<LearningGoalInfo>>.SignedOut;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CallOutcome<IReadOnlyList<LearningGoalInfo>>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(GoalJson.Default.IReadOnlyListLearningGoalInfo, cancellationToken) is { } goals
                    ? CallOutcome<IReadOnlyList<LearningGoalInfo>>.Ok(goals)
                    : CallOutcome<IReadOnlyList<LearningGoalInfo>>.Failed();
            },
            cancellationToken);

    public Task<CallOutcome<LearningGoalInfo>> CreateAsync(NewLearningGoal goal, CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.PostAsJsonAsync(Path, goal, GoalJson.Default.NewLearningGoal, cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<LearningGoalInfo>.SignedOut;
                    case HttpStatusCode.BadRequest:
                        var (fields, traceId) = await FieldErrorsAsync(response, cancellationToken);
                        return fields.Count > 0
                            ? new CallOutcome<LearningGoalInfo>(CallResult.Rejected, FieldErrors: fields, Messages: [.. fields.Values])
                            : CallOutcome<LearningGoalInfo>.Failed(traceId);
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<LearningGoalInfo>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(GoalJson.Default.LearningGoalInfo, cancellationToken) is { } created
                    ? CallOutcome<LearningGoalInfo>.Ok(created)
                    : CallOutcome<LearningGoalInfo>.Failed();
            },
            cancellationToken);

    // The first message per field, with the field's name as Planning writes it whatever the case. The body can be read
    // only once.
    private static async Task<(IReadOnlyDictionary<string, string> Fields, string? TraceId)> FieldErrorsAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return (new Dictionary<string, string>(), null);
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync(GoalJson.Default.FieldProblem, cancellationToken);
            var fields = new Dictionary<string, string>();
            foreach (var (key, texts) in problem?.Errors ?? [])
            {
                var name = LearningGoalValues.Fields.All
                    .FirstOrDefault(known => string.Equals(known, key, StringComparison.OrdinalIgnoreCase));
                if (name is not null && texts?.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) is { } text)
                {
                    fields[name] = text;
                }
            }

            return (fields, ProblemTrace.Check(problem?.TraceId));
        }
        catch (JsonException)
        {
            return (new Dictionary<string, string>(), null);
        }
    }
}

internal sealed record FieldProblem(Dictionary<string, string[]?>? Errors = null, string? TraceId = null);

// Source-generated for the trimmed browser app. A goal without its id, kind or name fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(IReadOnlyList<LearningGoalInfo>))]
[JsonSerializable(typeof(LearningGoalInfo))]
[JsonSerializable(typeof(NewLearningGoal))]
[JsonSerializable(typeof(FieldProblem))]
internal sealed partial class GoalJson : JsonSerializerContext;