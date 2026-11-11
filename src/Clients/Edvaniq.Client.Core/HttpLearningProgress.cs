using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to LearningEngine through the web host, which wants a login.
public sealed class HttpLearningProgress(HttpClient http) : ILearningProgress
{
    public const string Path = "api/learningengine/mastery";

    public Task<CallOutcome<IReadOnlyList<TopicProgress>>> ListAsync(CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync(Path, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return CallOutcome<IReadOnlyList<TopicProgress>>.SignedOut;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CallOutcome<IReadOnlyList<TopicProgress>>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(ProgressJson.Default.MasteryListBody, cancellationToken) is { } body
                    ? CallOutcome<IReadOnlyList<TopicProgress>>.Ok([.. body.Topics.Select(topic => topic.ToProgress())])
                    : CallOutcome<IReadOnlyList<TopicProgress>>.Failed();
            },
            cancellationToken);
}

internal sealed record MasteryListBody(IReadOnlyList<MasteryEntryBody> Topics);

// States come as strings, so a new one from LearningEngine shows as unknown instead of breaking the page.
internal sealed record MasteryEntryBody(Guid TopicId, int Mastery, string? State = null, Guid? LastAnswerId = null)
{
    public TopicProgress ToProgress() => new(TopicId, Mastery, State switch
    {
        "new" => TopicState.New,
        "weakness" => TopicState.Weakness,
        "building" => TopicState.Building,
        "secure" => TopicState.Secure,
        _ => null,
    }, LastAnswerId);
}

// Source-generated for the trimmed browser app.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(MasteryListBody))]
internal sealed partial class ProgressJson : JsonSerializerContext;