using Edvaniq.Services.Planning.Application;

namespace Edvaniq.Services.Planning.Api;

// The default learning goal of the user from the token. POST, because the first call creates it. Content asks for it
// when a text comes in (#215), with the token of the user who sent the text.
internal static class LearningGoalEndpoints
{
    public static IEndpointRouteBuilder MapLearningGoalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/learning-goals/default", async (ILearningGoals goals, CancellationToken cancellationToken) =>
            new LearningGoalResponse((await goals.DefaultAsync(cancellationToken)).Id));

        return app;
    }
}

internal sealed record LearningGoalResponse(Guid Id);