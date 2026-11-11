using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// POST /learning-goals/default of Planning. The Api hands this client the caller's token, so Planning answers with that
// user's goal. The response type is ours, nothing of Planning is referenced (rule 6).
public sealed partial class PlanningLearningGoals(HttpClient http, ILogger<PlanningLearningGoals> logger) : IDefaultLearningGoal
{
    public static readonly Uri Address = new("https+http://planning-api");

    // The longest the user waits for the goal before the text is turned away.
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(8);

    public async Task<DefaultLearningGoal> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsync(new Uri("learning-goals/default", UriKind.Relative), null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var goal = await response.Content.ReadFromJsonAsync<LearningGoal>(JsonSerializerOptions.Web, cancellationToken);
            return goal is { Id: var id } && id != Guid.Empty
                ? new DefaultLearningGoal(id, goal.Level, goal.OutputLanguage)
                : throw new InvalidDataException("Planning answered without a goal.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a message could name the address.
            LogUnavailable(exception.GetType().Name);
            throw new LearningGoalUnavailableException(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planning could not be asked for the default learning goal ({Error})")]
    private partial void LogUnavailable(string error);

    private sealed record LearningGoal(Guid Id, string? Level, string? OutputLanguage);
}