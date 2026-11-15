using Edvaniq.Services.Assessment.Application;

using Microsoft.AspNetCore.Mvc;

namespace Edvaniq.Services.Assessment.Api;

// The answers given on a block of the plan and in its sessions (#403), for Planning's session result. Block and session
// ids come from Planning, Assessment only uses them as keys with the owner from the token.
internal static class AnswerEndpoints
{
    // More than a block's sessions ever are: one per break-off, and a block's quiz has at most BlockQuizOpener.MaxSize
    // questions.
    public const int MaxSessions = 20;

    public static IEndpointRouteBuilder MapAnswerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/answers", async (
            Guid? blockId, [FromQuery(Name = "sessionId")] Guid[]? sessionIds, IAttempts attempts, CancellationToken cancellationToken) =>
        {
            var sessions = sessionIds?.Distinct().ToList() ?? [];
            if (blockId is null && sessions.Count == 0)
            {
                return Invalid("blockId", "Es fehlen Block und Session.");
            }

            if (sessions.Count > MaxSessions)
            {
                return Invalid("sessionId", $"Höchstens {MaxSessions} Sessions.");
            }

            var answered = await attempts.AnsweredInAsync(blockId, sessions, cancellationToken);
            return Results.Ok(new AnswerListResponse(
                [.. answered.Select(attempt => new AnswerEntry(attempt.Id, attempt.TopicId, attempt.Correct == true, attempt.AnsweredAt!.Value))]));
        });

        return app;
    }

    private static IResult Invalid(string field, string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [error] });
}

// An object, not a bare list, so fields can be added later. AnswerId is the answerId of the learning record, Correct
// false also for a shown solution, AnsweredAt UTC.
internal sealed record AnswerListResponse(IReadOnlyList<AnswerEntry> Answers);

internal sealed record AnswerEntry(Guid AnswerId, Guid TopicId, bool Correct, DateTime AnsweredAt);