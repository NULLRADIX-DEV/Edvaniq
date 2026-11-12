using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Assessment.Application;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Assessment.Infrastructure;

// GET /mastery/weak-and-secure?learningGoalId= of LearningEngine, a query by rule 4: LearningEngine listens to
// Assessment, so Assessment asks instead of listening back (rule 6). The Api hands this client the caller's token, so
// LearningEngine's owner filter decides whose topics they are. The response type is ours, and the weaknesses are taken
// in the order LearningEngine gives, Assessment never works a state out (#367).
public sealed partial class LearningEngineWeaknesses(HttpClient http, ILogger<LearningEngineWeaknesses> logger) : IWeaknesses
{
    public static readonly Uri Address = new("https+http://learningengine-api");

    public async Task<IReadOnlyList<Guid>?> OfLearningGoalAsync(Guid learningGoalId, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await http.GetFromJsonAsync<WeakAndSecure>(
                new Uri($"mastery/weak-and-secure?learningGoalId={learningGoalId}", UriKind.Relative), JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("LearningEngine answered without a body.");
            return [.. answer.Weaknesses.Select(weakness => weakness.TopicId)];
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Type and status only: a message could name the address, a body could carry anything.
            LogUnavailable(exception.GetType().Name, (exception as HttpRequestException)?.StatusCode is { } status ? (int)status : null);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "LearningEngine could not be asked for the weaknesses ({Error}, status {Status})")]
    private partial void LogUnavailable(string error, int? status);

    private sealed record WeakAndSecure(IReadOnlyList<Weakness> Weaknesses);

    private sealed record Weakness(Guid TopicId);
}