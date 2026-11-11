using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

internal static partial class ContentLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Splitting documents failed, trying again in {Delay}")]
    public static partial void SplitRoundFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Document {DocumentId} could not be split and is marked as failed")]
    public static partial void SplitFailed(ILogger logger, Exception exception, Guid documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzing documents failed, trying again in {Delay}")]
    public static partial void AnalysisRoundFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI could not analyze document {DocumentId}, it is marked as failed")]
    public static partial void AnalysisFailed(ILogger logger, Exception exception, Guid documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI gave no usable topics for document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void AnalysisAnswerUnusable(ILogger logger, Guid documentId, int attempts);

    // What one AI call for a document cost, every attempt counted, failed or not. The rows in AiAttempts outlast the log.
    [LoggerMessage(Level = LogLevel.Information, Message = "AI usage for the {Task} of document {DocumentId}: {Attempts} attempts, {InputTokens} input tokens, {OutputTokens} output tokens, cost {Cost} ({UnpricedAttempts} without price), {DurationMs} ms, model {Model}")]
    public static partial void AiUsage(
        ILogger logger, string task, Guid documentId, int attempts, long inputTokens, long outputTokens, decimal cost, int unpricedAttempts, long durationMs, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI usage for the {Task} of document {DocumentId} could not be stored, the analysis goes on")]
    public static partial void AiUsageNotStored(ILogger logger, Exception exception, string task, Guid documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI could not make questions for document {DocumentId}, it is marked as failed")]
    public static partial void QuestionsFailed(ILogger logger, Exception exception, Guid documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI gave no valid question for a topic of document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void QuestionsAnswerUnusable(ILogger logger, Guid documentId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Making question pools failed, trying again in {Delay}")]
    public static partial void PoolRoundFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge could not be asked for the topics of document {DocumentId} ({Error}), asking again")]
    public static partial void KnowledgeUnavailable(ILogger logger, Guid documentId, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Knowledge has no topics of document {DocumentId} long after its analysis, it is marked as failed")]
    public static partial void TopicsMissing(ILogger logger, Guid documentId);
}