using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// Who the prompts of a document write for, asked from Planning right before the AI step. When Planning can't say, the
// document fails with a reason instead of getting defaults, before any AI call is paid for.
internal static class GoalContext
{
    // Null if the document failed, the caller saves it then.
    public static async Task<LearnerProfile?> ProfileOfAsync(
        Document document, IGoalProfiles profiles, ILogger logger, CancellationToken cancellationToken)
    {
        // Documents from before learning goals (#215) have none to ask about.
        if (document.LearningGoalId is not { } goal || goal == Guid.Empty)
        {
            return LearnerProfile.Default;
        }

        try
        {
            if (await profiles.OfAsync(goal, cancellationToken) is { } profile)
            {
                return profile;
            }

            ContentLog.GoalGone(logger, document.Id);
            document.Fail(DocumentFailures.GoalGone);
        }
        catch (LearningGoalUnavailableException)
        {
            ContentLog.GoalUnavailable(logger, document.Id);
            document.Fail(DocumentFailures.GoalUnavailable);
        }

        return null;
    }
}