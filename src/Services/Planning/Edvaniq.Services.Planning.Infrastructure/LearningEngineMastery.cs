using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Infrastructure;

// GET /mastery?learningGoalId= of LearningEngine, a query by rule 4. The Api hands this client the caller's token, so
// LearningEngine's owner filter decides whose topics they are. The response type is ours, nothing of LearningEngine is
// referenced (rule 6). Mastery and state are taken as they come, Planning never works a state out (#367).
public sealed partial class LearningEngineMastery(HttpClient http, ILogger<LearningEngineMastery> logger) : IMasterySource
{
    public static readonly Uri Address = new("https+http://learningengine-api");

    public async Task<IReadOnlyDictionary<Guid, TopicStanding>?> OfLearningGoalAsync(Guid learningGoalId, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await http.GetFromJsonAsync<MasteryList>(
                new Uri($"mastery?learningGoalId={learningGoalId}", UriKind.Relative), JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("LearningEngine answered without a body.");
            return answer.Topics.ToDictionary(topic => topic.TopicId, topic => new TopicStanding(topic.Mastery, StateOf(topic.State)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Type and status only: a message could name the address, a body could carry anything.
            LogUnavailable(exception.GetType().Name, (exception as HttpRequestException)?.StatusCode is { } status ? (int)status : null);
            return null;
        }
    }

    // The one place that reads LearningEngine's state names. A name it doesn't know (a newer rule) gives null instead
    // of failing the whole plan.
    public static TopicState? StateOf(string? name) => name switch
    {
        "new" => TopicState.New,
        "weakness" => TopicState.Weakness,
        "building" => TopicState.Building,
        "secure" => TopicState.Secure,
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "LearningEngine could not be asked for the mastery ({Error}, status {Status})")]
    private partial void LogUnavailable(string error, int? status);

    private sealed record MasteryList(int RuleVersion, IReadOnlyList<TopicMastery> Topics);

    private sealed record TopicMastery(Guid TopicId, int Mastery, string? State);
}