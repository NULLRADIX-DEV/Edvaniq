using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Infrastructure;

// GET /mastery/changes of LearningEngine (#403), a query by rule 4 like LearningEngineMastery, with the caller's token.
// The response type is ours, the states are read by LearningEngineMastery.StateOf.
public sealed partial class LearningEngineMasteryChanges(HttpClient http, ILogger<LearningEngineMasteryChanges> logger) : IMasteryChanges
{
    public async Task<MasteryChanges?> BetweenAsync(
        IReadOnlyCollection<Guid> topicIds, SessionWindow window, IReadOnlyCollection<Guid> answerIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        var query = string.Join('&', [
            .. topicIds.Select(id => $"topicId={id}"),
            .. answerIds.Select(id => $"answerId={id}"),
            $"from={Moment(window.From)}",
            $"to={Moment(window.To)}",
        ]);
        try
        {
            var answer = await http.GetFromJsonAsync<ChangeList>(
                new Uri($"mastery/changes?{query}", UriKind.Relative), JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("LearningEngine answered without a body.");
            return new MasteryChanges(
                [.. answer.Topics.Select(topic => new TopicChange(topic.TopicId, Of(topic.Before), Of(topic.After), topic.Introduced))],
                answer.MissingAnswers);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Type and status only: a message could name the address, a body could carry anything.
            LogUnavailable(exception.GetType().Name, (exception as HttpRequestException)?.StatusCode is { } status ? (int)status : null);
            return null;
        }
    }

    // UTC with its Z, to the tick, so LearningEngine compares with the same moment.
    private static string Moment(DateTime value) =>
        Uri.EscapeDataString(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));

    private static TopicStanding? Of(Standing? standing) =>
        standing is null ? null : new TopicStanding(standing.Mastery, LearningEngineMastery.StateOf(standing.State));

    [LoggerMessage(Level = LogLevel.Warning, Message = "LearningEngine could not be asked for the mastery changes ({Error}, status {Status})")]
    private partial void LogUnavailable(string error, int? status);

    private sealed record ChangeList(int RuleVersion, int MissingAnswers, IReadOnlyList<Change> Topics);

    private sealed record Change(Guid TopicId, Standing? Before, Standing? After, bool Introduced);

    private sealed record Standing(int Mastery, string? State);
}