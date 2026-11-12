using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Api;

// Seeing and editing the user's topics: renaming, adding on two levels, hiding and showing again. The owner never appears
// in a request or a response: it comes from the token, and the owner filter applies it.
internal static class TopicEndpoints
{
    public static IEndpointRouteBuilder MapTopicEndpoints(this IEndpointRouteBuilder app)
    {
        var topics = app.MapGroup("/topics");

        // Hidden topics only on request, for bringing one back.
        topics.MapGet("/", async (Guid? learningGoalId, bool? includeHidden, ITopics topics, CancellationToken cancellationToken) =>
            TopicTreeResponse.From(await topics.ListAsync(learningGoalId, cancellationToken), includeHidden ?? false));

        // Someone else's topic and one that does not exist look the same: 404. A hidden one is there, with hidden: true.
        topics.MapGet("/{id:guid}", async (Guid id, ITopics topics, CancellationToken cancellationToken) =>
            await topics.FindAsync(id, cancellationToken) is { } topic
                ? Results.Ok(await TopicResponse.FromAsync(topic, topics, cancellationToken))
                : Results.NotFound());

        topics.MapPost("/", async (NewTopic request, ITopics topics, CancellationToken cancellationToken) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (Topic.CheckName(request.Name) is { } nameError)
            {
                errors[nameof(request.Name)] = [nameError];
            }

            // Exactly one of the two: a subtopic belongs to the learning goal of its topic.
            if ((request.ParentId is null) == (request.LearningGoalId is null || request.LearningGoalId == Guid.Empty))
            {
                errors[nameof(request.LearningGoalId)] = ["Gib entweder ein Lernziel oder ein übergeordnetes Thema an."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var added = await topics.AddAsync(request.LearningGoalId ?? Guid.Empty, request.ParentId, request.Name!, cancellationToken);
            return added.Topic is { } topic
                ? Results.Created($"/topics/{topic.Id}", await TopicResponse.FromAsync(topic, topics, cancellationToken))
                : Failure(added.Error);
        });

        topics.MapPatch("/{id:guid}", async (Guid id, RenamedTopic request, ITopics topics, CancellationToken cancellationToken) =>
        {
            if (Topic.CheckName(request.Name) is { } error)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Name)] = [error] });
            }

            var renamed = await topics.RenameAsync(id, request.Name!, cancellationToken);
            return renamed.Topic is { } topic
                ? Results.Ok(await TopicResponse.FromAsync(topic, topics, cancellationToken))
                : Failure(renamed.Error);
        });

        // Hiding takes the subtopics along, showing brings them back in their own state. Both may be repeated.
        topics.MapPut("/{id:guid}/hidden", (Guid id, ITopics topics, CancellationToken cancellationToken) =>
            SetHiddenAsync(id, true, topics, cancellationToken));
        topics.MapDelete("/{id:guid}/hidden", (Guid id, ITopics topics, CancellationToken cancellationToken) =>
            SetHiddenAsync(id, false, topics, cancellationToken));

        return app;
    }

    private static async Task<IResult> SetHiddenAsync(Guid id, bool hidden, ITopics topics, CancellationToken cancellationToken)
    {
        var changed = await topics.SetHiddenAsync(id, hidden, cancellationToken);
        return changed.Topic is { } topic
            ? Results.Ok(await TopicResponse.FromAsync(topic, topics, cancellationToken))
            : Failure(changed.Error);
    }

    // The messages name no other topic, only what the user can do.
    private static IResult Failure(TopicError error) => error switch
    {
        TopicError.NameTaken => Results.Problem(
            statusCode: StatusCodes.Status409Conflict, detail: "Ein Thema mit diesem Namen gibt es hier schon, auch wenn es ausgeblendet ist."),
        TopicError.ParentHidden => Results.Problem(
            statusCode: StatusCodes.Status409Conflict, detail: "Das übergeordnete Thema ist ausgeblendet. Blende zuerst das übergeordnete Thema ein."),
        TopicError.ParentIsSubtopic => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(NewTopic.ParentId)] = ["Ein Unterthema hat keine Unterthemen. Themen haben zwei Ebenen."],
        }),
        _ => Results.NotFound(),
    };
}

internal sealed record RenamedTopic(string? Name);

internal sealed record NewTopic(string? Name, Guid? LearningGoalId, Guid? ParentId);

// DocumentId and Position name the analysis the topic came from, the client shows which topics a document brought. Hidden
// also for a subtopic whose topic is hidden.
internal sealed record TopicResponse(Guid Id, Guid LearningGoalId, Guid? ParentId, string Name, Guid? DocumentId, int Position, bool Hidden)
{
    public static async Task<TopicResponse> FromAsync(Topic topic, ITopics topics, CancellationToken cancellationToken)
    {
        var parent = topic.ParentId is { } parentId ? await topics.FindAsync(parentId, cancellationToken) : null;
        return new(topic.Id, topic.LearningGoalId, topic.ParentId, topic.Name, topic.DocumentId, topic.Position, topic.IsHiddenUnder(parent));
    }
}

// A topic of the upper level with its subtopics.
internal sealed record TopicTreeResponse(
    Guid Id, Guid LearningGoalId, string Name, Guid? DocumentId, int Position, bool Hidden, IReadOnlyList<SubtopicResponse> Subtopics)
{
    // Keeps the order of the list on both levels. Without hidden ones a hidden topic goes with all its subtopics.
    public static IEnumerable<TopicTreeResponse> From(IReadOnlyList<Topic> topics, bool includeHidden)
    {
        var subtopics = topics.Where(topic => topic.IsSubtopic).ToLookup(topic => topic.ParentId);

        return topics
            .Where(topic => !topic.IsSubtopic && (includeHidden || !topic.Hidden))
            .Select(topic => new TopicTreeResponse(
                topic.Id,
                topic.LearningGoalId,
                topic.Name,
                topic.DocumentId,
                topic.Position,
                topic.Hidden,
                [.. subtopics[topic.Id]
                    .Where(subtopic => includeHidden || !subtopic.Hidden)
                    .Select(subtopic => new SubtopicResponse(
                        subtopic.Id, subtopic.Name, subtopic.DocumentId, subtopic.Position, subtopic.IsHiddenUnder(topic)))]));
    }
}

internal sealed record SubtopicResponse(Guid Id, string Name, Guid? DocumentId, int Position, bool Hidden);