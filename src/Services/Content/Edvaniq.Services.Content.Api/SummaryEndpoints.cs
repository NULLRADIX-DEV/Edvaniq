using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Api;

// The summary of a topic in one length (#344). POST asks for it: the worker makes it once, the client reads it with GET
// until it is ready. The owner comes from the token, Knowledge decides whether the topic is the caller's.
internal static class SummaryEndpoints
{
    public const string NoMaterial = "Zu diesem Thema gibt es noch kein Material, aus dem eine Zusammenfassung entstehen kann.";

    public const string AiPaused = "Zusammenfassungen sind gerade pausiert. Versuch es später noch einmal.";

    public const string KnowledgeUnavailable = "Die Themen sind gerade nicht erreichbar. Versuch es gleich noch einmal.";

    public static IEndpointRouteBuilder MapSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        var summaries = app.MapGroup("/topics/{topicId:guid}/summaries");

        summaries.MapGet("/{length}", async (Guid topicId, string length, ITopicSummaries summaries, CancellationToken cancellationToken) =>
            LengthOf(length) is not { } parsed
                ? UnknownLength()
                : await summaries.FindAsync(topicId, parsed, cancellationToken) is { } view
                    ? Results.Ok(SummaryResponse.From(view))
                    : Results.NotFound());

        // A ready or pending summary comes back as it is, a failed one goes to the worker again. Only a new or retried one
        // checks the AI first: no job for it while it is paused or this month's budget is used up.
        summaries.MapPost("/{length}", async (
            Guid topicId,
            string length,
            ITopicSummaries summaries,
            IAiSwitch aiSwitch,
            IAiBudget budget,
            CancellationToken cancellationToken) =>
        {
            if (LengthOf(length) is not { } parsed)
            {
                return UnknownLength();
            }

            var existing = await summaries.FindAsync(topicId, parsed, cancellationToken);
            if (existing?.Summary.Status != SummaryStatus.Ready && existing?.Summary.Status != SummaryStatus.Pending)
            {
                if (await aiSwitch.IsPausedAsync(cancellationToken))
                {
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: AiPaused);
                }

                if (await BudgetRefusal.OfAsync(budget, cancellationToken) is { } refused)
                {
                    return refused;
                }
            }

            try
            {
                var (result, view) = await summaries.RequestAsync(topicId, parsed, cancellationToken);
                return result switch
                {
                    SummaryRequestResult.TopicNotFound => Results.NotFound(),
                    SummaryRequestResult.NoMaterial => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: NoMaterial),
                    SummaryRequestResult.Existing when view!.Summary.Status == SummaryStatus.Ready => Results.Ok(SummaryResponse.From(view)),
                    _ => Results.Json(SummaryResponse.From(view!), statusCode: StatusCodes.Status202Accepted),
                };
            }
            catch (TopicMaterialUnavailableException)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: KnowledgeUnavailable);
            }
        });

        return app;
    }

    private static SummaryLength? LengthOf(string length) => length switch
    {
        "short" => SummaryLength.Short,
        "standard" => SummaryLength.Standard,
        "detailed" => SummaryLength.Detailed,
        _ => null,
    };

    private static IResult UnknownLength() => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["length"] = ["Die Länge ist short, standard oder detailed."] });
}

// Status is pending, ready or failed. Text, model and prompt version come once it is ready, a failed one names its
// reason on the fixed list of #283. AiGenerated is always true, the client labels the text with it.
internal sealed record SummaryResponse(
    Guid TopicId,
    string Length,
    string Status,
    string? Text,
    string? FailureCode,
    bool AiGenerated,
    string? Model,
    string? PromptVersion,
    DateTime CreatedAt,
    DateTime? ReadyAt,
    IReadOnlyList<SourceResponse> Sources)
{
    public static SummaryResponse From(SummaryView view)
    {
        var summary = view.Summary;
        return new(
            summary.TopicId,
            summary.Length.ToString().ToLowerInvariant(),
            summary.Status.ToString().ToLowerInvariant(),
            summary.Text,
            summary.FailureCode?.ToString(),
            AiGenerated: true,
            summary.Model,
            summary.PromptVersion,
            summary.CreatedAt,
            summary.ReadyAt,
            [.. view.Sources.Select(source => new SourceResponse(source.DocumentId, source.DocumentTitle, source.PageFrom, source.PageTo))]);
    }
}

// The pages are null for pasted text.
internal sealed record SourceResponse(Guid DocumentId, string DocumentTitle, int? PageFrom, int? PageTo);