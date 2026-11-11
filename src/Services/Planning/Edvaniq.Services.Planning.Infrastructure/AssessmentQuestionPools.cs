using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Planning.Application;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Infrastructure;

// GET /question-pools?learningGoalId= of Assessment, a query by rule 4. The Api hands this client the caller's token, so
// Assessment's owner filter decides whose topics they are. The response type is ours, nothing of Assessment is
// referenced. Whether a pool is ready is Assessment's rule: Planning reads only the status it names, never the count.
public sealed partial class AssessmentQuestionPools(HttpClient http, ILogger<AssessmentQuestionPools> logger) : IQuestionPoolSource
{
    public static readonly Uri Address = new("https+http://assessment-api");

    public async Task<IReadOnlySet<Guid>?> ReadyTopicsAsync(Guid learningGoalId, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await http.GetFromJsonAsync<PoolList>(
                new Uri($"question-pools?learningGoalId={learningGoalId}", UriKind.Relative), JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("Assessment answered without a body.");
            return answer.Topics.Where(topic => IsReady(topic.Status)).Select(topic => topic.TopicId).ToHashSet();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Type and status only: a message could name the address, a body could carry anything.
            LogUnavailable(exception.GetType().Name, (exception as HttpRequestException)?.StatusCode is { } status ? (int)status : null);
            return null;
        }
    }

    // The one place that reads Assessment's status names. Anything but "ready" (a newer status too) isn't ready.
    public static bool IsReady(string? status) => status == "ready";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assessment could not be asked for the question pools ({Error}, status {Status})")]
    private partial void LogUnavailable(string error, int? status);

    private sealed record PoolList(IReadOnlyList<TopicPool> Topics);

    private sealed record TopicPool(Guid TopicId, string? Status);
}