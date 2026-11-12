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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI is unavailable for the {Task} of document {DocumentId} ({Error}), outage {Outages}, asking again at {NextAttemptAt}")]
    public static partial void AiUnavailable(ILogger logger, string task, Guid documentId, string error, int outages, DateTime? nextAttemptAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI stayed unavailable for the {Task} of document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void AiStaysUnavailable(ILogger logger, string task, Guid documentId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Planning could not say the profile of the learning goal of document {DocumentId}, it is marked as failed")]
    public static partial void GoalUnavailable(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The learning goal of document {DocumentId} is gone, it is marked as failed")]
    public static partial void GoalGone(ILogger logger, Guid documentId);

    // The two lines the operator gets an alert for. Event names and the "AI budget:" prefix stay, the platform matches
    // them. Only the percentage, never the budget or an amount.
    [LoggerMessage(EventId = 3031, EventName = "AiBudgetWarning", Level = LogLevel.Warning, Message = "AI budget: {Percent} % of this month's budget used")]
    public static partial void AiBudgetWarning(ILogger logger, int percent);

    [LoggerMessage(EventId = 3032, EventName = "AiBudgetExhausted", Level = LogLevel.Error, Message = "AI budget: this month's budget is used up ({Percent} %), AI calls are refused until next month")]
    public static partial void AiBudgetExhausted(ILogger logger, int percent);

    [LoggerMessage(EventId = 3033, EventName = "AiBudgetMissing", Level = LogLevel.Warning, Message = "AI budget: no monthly budget is set, the AI has no limit")]
    public static partial void AiBudgetMissing(ILogger logger);

    [LoggerMessage(EventId = 3034, EventName = "AiBudgetUnknown", Level = LogLevel.Warning, Message = "AI budget: could not be read at the start ({Error})")]
    public static partial void AiBudgetUnknown(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "The AI budget is used up, the {Task} of document {DocumentId} is marked as failed")]
    public static partial void AiBudgetRefused(ILogger logger, string task, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The AI is paused, the {Task} of document {DocumentId} is marked as failed")]
    public static partial void AiPaused(ILogger logger, string task, Guid documentId);

    // A wrong key or settings fail every document alike, this is the line for whoever runs the app.
    [LoggerMessage(Level = LogLevel.Error, Message = "The AI provider refused the {Task} of document {DocumentId} with status {Status} ({Error}), it is marked as failed. Check the key and the AI settings")]
    public static partial void AiRefused(ILogger logger, string task, Guid documentId, int? status, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI gave no usable topics for document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void AnalysisAnswerUnusable(ILogger logger, Guid documentId, int attempts);

    // What one AI call for a document cost, every attempt counted, failed or not. The rows in AiAttempts outlast the log.
    [LoggerMessage(Level = LogLevel.Information, Message = "AI usage for the {Task} of document {DocumentId}: {Attempts} attempts, {InputTokens} input tokens, {OutputTokens} output tokens, cost {Cost} ({UnpricedAttempts} without price), {DurationMs} ms, model {Model}")]
    public static partial void AiUsage(
        ILogger logger, string task, Guid documentId, int attempts, long inputTokens, long outputTokens, decimal cost, int unpricedAttempts, long durationMs, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The AI usage for the {Task} of document {DocumentId} could not be stored, the analysis goes on")]
    public static partial void AiUsageNotStored(ILogger logger, Exception exception, string task, Guid documentId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI gave no valid question for a topic of document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void QuestionsAnswerUnusable(ILogger logger, Guid documentId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Making question pools failed, trying again in {Delay}")]
    public static partial void PoolRoundFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge could not be asked for the topics of document {DocumentId} ({Error}), asking again")]
    public static partial void KnowledgeUnavailable(ILogger logger, Guid documentId, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Knowledge has no topics of document {DocumentId} long after its analysis, it is marked as failed")]
    public static partial void TopicsMissing(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId} was deleted during its {Step}, nothing of it was saved")]
    public static partial void DocumentDeletedMeanwhile(ILogger logger, string step, Guid documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sweeping the file store failed, trying again in {Delay}")]
    public static partial void SweepFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "File {FileId} had no row and was removed from the file store")]
    public static partial void FileSwept(ILogger logger, Guid fileId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading files failed, trying again in {Delay}")]
    public static partial void ReadRoundFailed(ILogger logger, Exception exception, TimeSpan delay);

    // The type only: a message of the parser could quote the file.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The file of document {DocumentId} could not be read ({Error}), it is marked as failed")]
    public static partial void FileUnreadable(ILogger logger, Guid documentId, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The file of document {DocumentId} is missing from the file store, it is marked as failed")]
    public static partial void FileMissing(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the file of document {DocumentId} broke off too often, it is marked as failed")]
    public static partial void FileGaveUp(ILogger logger, Guid documentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Read {Pages} pages and {Characters} characters from the file of document {DocumentId} in {DurationMs} ms")]
    public static partial void FileRead(ILogger logger, Guid documentId, int pages, int characters, long durationMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Topic {TopicId} has no material in document {DocumentId}, it gets no questions from it")]
    public static partial void TopicWithoutMaterial(ILogger logger, Guid topicId, Guid documentId);
}