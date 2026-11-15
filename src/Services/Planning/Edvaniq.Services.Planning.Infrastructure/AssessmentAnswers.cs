using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Infrastructure;

// GET /answers?blockId=&sessionId= of Assessment (#403), a query by rule 4 with the caller's token, so Assessment's owner
// filter decides whose answers they are. The response type is ours, nothing of Assessment is referenced.
public sealed partial class AssessmentAnswers(HttpClient http, ILogger<AssessmentAnswers> logger) : ISessionAnswers
{
    public static readonly Uri Address = new("https+http://assessment-api");

    public async Task<IReadOnlyList<SessionAnswer>?> OfBlockAsync(
        Guid blockId, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var query = string.Join('&', sessionIds.Select(id => $"sessionId={id}").Prepend($"blockId={blockId}"));
        try
        {
            var answer = await http.GetFromJsonAsync<AnswerList>(
                new Uri($"answers?{query}", UriKind.Relative), JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("Assessment answered without a body.");
            return [.. answer.Answers.Select(entry => new SessionAnswer(
                entry.AnswerId, entry.TopicId, entry.Correct, DateTime.SpecifyKind(entry.AnsweredAt, DateTimeKind.Utc)))];
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Type and status only: a message could name the address, a body could carry anything.
            LogUnavailable(exception.GetType().Name, (exception as HttpRequestException)?.StatusCode is { } status ? (int)status : null);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assessment could not be asked for the answers of a block ({Error}, status {Status})")]
    private partial void LogUnavailable(string error, int? status);

    private sealed record AnswerList(IReadOnlyList<Entry> Answers);

    private sealed record Entry(Guid AnswerId, Guid TopicId, bool Correct, DateTime AnsweredAt);
}