using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Asks Content through the web host, which wants a login, like HttpWeekPlan.
public sealed class HttpMaterialQuota(HttpClient http) : IMaterialQuota
{
    public const string Path = "api/content/quota";

    public Task<CallOutcome<MaterialQuota>> GetAsync(CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync(Path, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return CallOutcome<MaterialQuota>.SignedOut;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return CallOutcome<MaterialQuota>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(QuotaJson.Default.QuotaBody, cancellationToken) is { } body
                    ? CallOutcome<MaterialQuota>.Ok(new MaterialQuota(
                        body.PagesLeftToday, body.PagesPerDay, body.GenerationsLeftToday, body.GenerationsPerDay, body.ResetsAt))
                    : CallOutcome<MaterialQuota>.Failed();
            },
            cancellationToken);
}

internal sealed record QuotaBody(int PagesLeftToday, int PagesPerDay, int? GenerationsLeftToday, int? GenerationsPerDay, DateTime ResetsAt);

// Source-generated for the trimmed browser app. An answer without the page counts fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(QuotaBody))]
internal sealed partial class QuotaJson : JsonSerializerContext;