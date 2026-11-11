using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Api;

// Seeing and renaming the user's topics. They come from the analysis of the material, there is no way to add one here.
// The owner never appears in a request or a response: it comes from the token, and the owner filter applies it.
internal static class TopicEndpoints
{
    public static IEndpointRouteBuilder MapTopicEndpoints(this IEndpointRouteBuilder app)
    {
        var topics = app.MapGroup("/topics");

        topics.MapGet("/", async (Guid? learningGoalId, ITopics topics, CancellationToken cancellationToken) =>
            TopicTreeResponse.From(await topics.ListAsync(learningGoalId, cancellationToken)));

        // Someone else's topic and one that does not exist look the same: 404.
        topics.MapGet("/{id:guid}", async (Guid id, ITopics topics, CancellationToken cancellationToken) =>
            await topics.FindAsync(id, cancellationToken) is { } topic
                ? Results.Ok(TopicResponse.From(topic))
                : Results.NotFound());

        topics.MapPatch("/{id:guid}", async (Guid id, RenamedTopic request, ITopics topics, CancellationToken cancellationToken) =>
        {
            if (Topic.CheckName(request.Name) is { } error)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Name)] = [error] });
            }

            return await topics.RenameAsync(id, request.Name!, cancellationToken) is { } topic
                ? Results.Ok(TopicResponse.From(topic))
                : Results.NotFound();
        });

        return app;
    }
}

internal sealed record RenamedTopic(string? Name);

// DocumentId and Position name the analysis the topic came from, the client shows which topics a document brought.
internal sealed record TopicResponse(Guid Id, Guid LearningGoalId, Guid? ParentId, string Name, Guid? DocumentId, int Position)
{
    public static TopicResponse From(Topic topic) =>
        new(topic.Id, topic.LearningGoalId, topic.ParentId, topic.Name, topic.DocumentId, topic.Position);
}

// A topic of the upper level with its subtopics.
internal sealed record TopicTreeResponse(
    Guid Id, Guid LearningGoalId, string Name, Guid? DocumentId, int Position, IReadOnlyList<SubtopicResponse> Subtopics)
{
    // Keeps the order of the list on both levels.
    public static IEnumerable<TopicTreeResponse> From(IReadOnlyList<Topic> topics)
    {
        var subtopics = topics.Where(topic => topic.IsSubtopic).ToLookup(topic => topic.ParentId);

        return topics.Where(topic => !topic.IsSubtopic).Select(topic => new TopicTreeResponse(
            topic.Id,
            topic.LearningGoalId,
            topic.Name,
            topic.DocumentId,
            topic.Position,
            [.. subtopics[topic.Id].Select(subtopic => new SubtopicResponse(subtopic.Id, subtopic.Name, subtopic.DocumentId, subtopic.Position))]));
    }
}

internal sealed record SubtopicResponse(Guid Id, string Name, Guid? DocumentId, int Position);