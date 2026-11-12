using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Planning through the web host, which wants a login. The HttpClient carries the base address of the host and
// keeps the session cookie.
public sealed class HttpDailyBudget(HttpClient http) : IDailyBudget
{
    public const string Path = "api/planning/daily-budget";

    public Task<CallOutcome<DailyBudgetInfo>> GetAsync(CancellationToken cancellationToken = default) =>
        CallAsync(token => http.GetAsync(Path, token), cancellationToken);

    public Task<CallOutcome<DailyBudgetInfo>> SetAsync(int minutes, CancellationToken cancellationToken = default) =>
        CallAsync(
            token => http.PutAsJsonAsync(Path, new BudgetWeek(minutes, minutes, minutes, minutes, minutes, minutes, minutes), BudgetJson.Default.BudgetWeek, token),
            cancellationToken);

    private static Task<CallOutcome<DailyBudgetInfo>> CallAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await send(cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<DailyBudgetInfo>.SignedOut;
                    case HttpStatusCode.BadRequest:
                        var (messages, traceId) = await RejectionAsync(response, cancellationToken);
                        return messages.Count > 0
                            ? new CallOutcome<DailyBudgetInfo>(CallResult.Rejected, Messages: messages)
                            : CallOutcome<DailyBudgetInfo>.Failed(traceId);
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<DailyBudgetInfo>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(BudgetJson.Default.BudgetBody, cancellationToken) is { } body
                    ? CallOutcome<DailyBudgetInfo>.Ok(body.ToInfo())
                    : CallOutcome<DailyBudgetInfo>.Failed();
            },
            cancellationToken);

    // Planning names each day with the same message, the page shows it once. The body can be read only once.
    private static async Task<(IReadOnlyList<string> Messages, string? TraceId)> RejectionAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return ([], null);
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync(BudgetJson.Default.BudgetProblem, cancellationToken);
            IReadOnlyList<string> messages = problem?.Errors is { } errors
                ? [.. errors.Values.SelectMany(texts => texts ?? []).Where(text => !string.IsNullOrWhiteSpace(text)).Distinct()]
                : [];
            return (messages, ProblemTrace.Check(problem?.TraceId));
        }
        catch (JsonException)
        {
            return ([], null);
        }
    }
}

internal sealed record BudgetWeek(int Monday, int Tuesday, int Wednesday, int Thursday, int Friday, int Saturday, int Sunday);

internal sealed record BudgetBody(int Monday, int Tuesday, int Wednesday, int Thursday, int Friday, int Saturday, int Sunday, bool IsDefault)
{
    public DailyBudgetInfo ToInfo() => new([Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday], IsDefault);
}

internal sealed record BudgetProblem(Dictionary<string, string[]?>? Errors = null, string? TraceId = null);

// Source-generated for the trimmed browser app. An answer without all seven days fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(BudgetWeek))]
[JsonSerializable(typeof(BudgetBody))]
[JsonSerializable(typeof(BudgetProblem))]
internal sealed partial class BudgetJson : JsonSerializerContext;