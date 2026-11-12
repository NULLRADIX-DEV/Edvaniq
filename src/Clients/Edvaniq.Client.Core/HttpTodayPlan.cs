using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Planning through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie.
public sealed class HttpTodayPlan(HttpClient http) : ITodayPlan
{
    public const string Path = "api/planning/plan/today";

    public Task<CallOutcome<TodayPlan>> GetAsync(CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync(Path, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return CallOutcome<TodayPlan>.SignedOut;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CallOutcome<TodayPlan>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(PlanJson.Default.PlanBody, cancellationToken) is { } body
                    ? CallOutcome<TodayPlan>.Ok(body.ToPlan())
                    : CallOutcome<TodayPlan>.Failed();
            },
            cancellationToken);
}

// Kinds and reasons come as strings, so a new one from Planning shows as unknown instead of breaking the page.
internal sealed record PlanBody(
    DateOnly StudyDay,
    IReadOnlyList<BlockBody> Blocks,
    bool MasteryAvailable,
    string? EmptyReason = null,
    IReadOnlyList<Guid>? ShortenedTopicIds = null,
    IReadOnlyList<Guid>? NotReadyTopicIds = null)
{
    public TodayPlan ToPlan() => new(
        StudyDay,
        [.. Blocks.Select(block => block.ToBlock())],
        Blocks.Count > 0 ? null : EmptyOf(EmptyReason),
        MasteryAvailable,
        ShortenedTopicIds ?? [],
        NotReadyTopicIds ?? []);

    public static PlanEmpty? EmptyOf(string? reason) => reason switch
    {
        "noTopics" => PlanEmpty.NoTopics,
        "questionsPending" => PlanEmpty.QuestionsPending,
        "allMastered" => PlanEmpty.AllMastered,
        _ => null,
    };
}

internal sealed record BlockBody(Guid TopicId, int Minutes, string? Kind = null)
{
    public PlanBlock ToBlock() => new(TopicId, Minutes, Kind switch
    {
        "learn" => BlockKind.Learn,
        "review" => BlockKind.Review,
        _ => null,
    });
}

// Source-generated for the trimmed browser app. A plan without its day, blocks or mastery flag fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PlanBody))]
internal sealed partial class PlanJson : JsonSerializerContext;