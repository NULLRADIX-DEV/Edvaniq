using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Content through the web host, which wants a login. Content puts the sentence for the user into the title of
// its problems (409, 429, 503), read with the same checks as for the documents (ProblemTitle).
public sealed class HttpTopicSummaries(HttpClient http) : ITopicSummaries
{
    public const string TopicsPath = "api/content/topics";

    public Task<CallOutcome<TopicSummary>> RequestAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken = default) =>
        CallAsync(token => http.PostAsync(PathOf(topicId, length), content: null, token), cancellationToken);

    public Task<CallOutcome<TopicSummary>> GetAsync(Guid topicId, SummaryLength length, CancellationToken cancellationToken = default) =>
        CallAsync(token => http.GetAsync(PathOf(topicId, length), token), cancellationToken);

    public static string PathOf(Guid topicId, SummaryLength length) =>
        $"{TopicsPath}/{topicId:D}/summaries/{length.ToString().ToLowerInvariant()}";

    private static Task<CallOutcome<TopicSummary>> CallAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await send(cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<TopicSummary>.SignedOut;
                    case HttpStatusCode.NotFound:
                        return new CallOutcome<TopicSummary>(CallResult.NotFound);
                    case HttpStatusCode.Conflict:
                        return await ProblemTitle.ReadAsync(response, cancellationToken) is { } noMaterial
                            ? new CallOutcome<TopicSummary>(CallResult.NotReady, Messages: [noMaterial])
                            : CallOutcome<TopicSummary>.Failed();
                    case HttpStatusCode.TooManyRequests:
                        return new CallOutcome<TopicSummary>(
                            CallResult.Limited, Messages: await ProblemTitle.ReadAsync(response, cancellationToken) is { } limit ? [limit] : []);
                    case HttpStatusCode.ServiceUnavailable:
                        // Content's 503 names why (AI paused, topics away). One without a sentence, from the gateway, is
                        // a connection problem.
                        return await ProblemTitle.ReadAsync(response, cancellationToken) is { } paused
                            ? new CallOutcome<TopicSummary>(CallResult.Limited, Messages: [paused])
                            : CallOutcome<TopicSummary>.Failed();
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<TopicSummary>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(SummaryJson.Default.SummaryBody, cancellationToken) is { } body
                    && body.ToSummary() is { } summary
                    ? CallOutcome<TopicSummary>.Ok(summary)
                    : CallOutcome<TopicSummary>.Failed();
            },
            cancellationToken);
}

internal sealed record SummaryBody(
    Guid TopicId,
    string Length,
    string Status,
    string? Text = null,
    string? FailureReason = null,
    bool AiGenerated = true,
    IReadOnlyList<SourceBody>? Sources = null)
{
    // Null for a length or status this version doesn't know, and for a ready one without text.
    public TopicSummary? ToSummary()
    {
        SummaryLength? length = Length switch
        {
            "short" => SummaryLength.Short,
            "standard" => SummaryLength.Standard,
            "detailed" => SummaryLength.Detailed,
            _ => null,
        };
        SummaryStatus? status = Status switch
        {
            "pending" => SummaryStatus.Pending,
            "ready" => SummaryStatus.Ready,
            "failed" => SummaryStatus.Failed,
            _ => null,
        };
        if (length is null || status is null || (status == SummaryStatus.Ready && string.IsNullOrWhiteSpace(Text)))
        {
            return null;
        }

        return new TopicSummary(
            TopicId,
            length.Value,
            status.Value,
            Text,
            string.IsNullOrWhiteSpace(FailureReason) ? null : FailureReason.Trim(),
            AiGenerated,
            [.. (Sources ?? []).Select(source => new SummarySource(
                source.DocumentId, source.DocumentTitle, source.PageFrom, source.PageTo, source.Origin, source.SectionId))]);
    }
}

internal sealed record SourceBody(
    Guid DocumentId, string DocumentTitle, int? PageFrom, int? PageTo, string? Origin = null, Guid? SectionId = null);

// Source-generated for the trimmed browser app. An answer without topic, length or status fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SummaryBody))]
internal sealed partial class SummaryJson : JsonSerializerContext;