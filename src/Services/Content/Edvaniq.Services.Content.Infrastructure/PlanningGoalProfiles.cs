using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// GET /internal/learning-goals/{id}/profile of Planning, with the worker's internal token: only level and output
// language, nothing to identify the learner by. The response type is ours, nothing of Planning is referenced (rule 6).
public sealed partial class PlanningGoalProfiles(HttpClient http, ILogger<PlanningGoalProfiles> logger) : IGoalProfiles
{
    public static readonly Uri Address = new("https+http://planning-api");

    public async Task<LearnerProfile?> OfAsync(Guid learningGoalId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri($"internal/learning-goals/{learningGoalId}/profile", UriKind.Relative), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var profile = await response.Content.ReadFromJsonAsync<Profile>(JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("Planning answered without a profile.");
            return LearnerProfile.Of(profile.Level, profile.OutputLanguage);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a message could name the address.
            LogUnavailable(exception.GetType().Name);
            throw new LearningGoalUnavailableException(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planning could not be asked for the profile of a learning goal ({Error})")]
    private partial void LogUnavailable(string error);

    private sealed record Profile(string? Level, string? OutputLanguage);
}