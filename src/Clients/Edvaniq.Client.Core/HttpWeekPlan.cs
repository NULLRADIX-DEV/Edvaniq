using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Planning through the web host, which wants a login, like HttpTodayPlan.
public sealed class HttpWeekPlan(HttpClient http) : IWeekPlan
{
    public const string Path = "api/planning/plan/week";

    public Task<CallOutcome<WeekPlan>> GetAsync(CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync(Path, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return CallOutcome<WeekPlan>.SignedOut;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CallOutcome<WeekPlan>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(WeekJson.Default.WeekBody, cancellationToken) is { Days.Count: > 0 } body
                    ? CallOutcome<WeekPlan>.Ok(body.ToPlan())
                    : CallOutcome<WeekPlan>.Failed();
            },
            cancellationToken);
}

internal sealed record WeekBody(
    IReadOnlyList<DayBody> Days,
    bool MasteryAvailable,
    string? EmptyReason = null,
    IReadOnlyList<Guid>? ShortenedTopicIds = null,
    IReadOnlyList<Guid>? NotReadyTopicIds = null)
{
    public WeekPlan ToPlan() => new(
        [.. Days.Select(day => new PlanDay(day.StudyDay, [.. day.Blocks.Select(block => block.ToBlock())]))],
        Days.Any(day => day.Blocks.Count > 0) ? null : PlanBody.EmptyOf(EmptyReason),
        MasteryAvailable,
        ShortenedTopicIds ?? [],
        NotReadyTopicIds ?? []);
}

internal sealed record DayBody(DateOnly StudyDay, IReadOnlyList<BlockBody> Blocks);

// Source-generated for the trimmed browser app. A week without its days or mastery flag fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(WeekBody))]
internal sealed partial class WeekJson : JsonSerializerContext;