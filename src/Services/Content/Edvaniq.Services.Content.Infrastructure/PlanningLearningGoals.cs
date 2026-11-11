using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// POST /learning-goals/default and GET /learning-goals/{id} of Planning. The Api hands this client the caller's token,
// so Planning answers with that user's goals. The response type is ours, nothing of Planning is referenced (rule 6).
public sealed partial class PlanningLearningGoals(HttpClient http, ILogger<PlanningLearningGoals> logger) : ILearningGoals
{
    public static readonly Uri Address = new("https+http://planning-api");

    // The longest the user waits for the goal before the text is turned away.
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(8);

    public Task<LearningGoalInfo> GetDefaultAsync(CancellationToken cancellationToken) =>
        AskAsync(
            async () =>
            {
                using var response = await http.PostAsync(new Uri("learning-goals/default", UriKind.Relative), null, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await ReadAsync(response, cancellationToken);
            },
            cancellationToken);

    // Planning answers 404 for a goal of someone else just as for one that doesn't exist.
    public Task<LearningGoalInfo?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        AskAsync<LearningGoalInfo?>(
            async () =>
            {
                using var response = await http.GetAsync(new Uri($"learning-goals/{id}", UriKind.Relative), cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                var goal = await ReadAsync(response, cancellationToken);
                return goal.Id == id ? goal : throw new InvalidDataException("Planning answered with another goal.");
            },
            cancellationToken);

    private static async Task<LearningGoalInfo> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var goal = await response.Content.ReadFromJsonAsync<LearningGoal>(JsonSerializerOptions.Web, cancellationToken);
        return goal is { Id: var id } && id != Guid.Empty
            ? new LearningGoalInfo(id, goal.Level, goal.OutputLanguage)
            : throw new InvalidDataException("Planning answered without a goal.");
    }

    private async Task<T> AskAsync<T>(Func<Task<T>> ask, CancellationToken cancellationToken)
    {
        try
        {
            return await ask();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a message could name the address.
            LogUnavailable(exception.GetType().Name);
            throw new LearningGoalUnavailableException(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planning could not be asked for the learning goal ({Error})")]
    private partial void LogUnavailable(string error);

    private sealed record LearningGoal(Guid Id, string? Level, string? OutputLanguage);
}