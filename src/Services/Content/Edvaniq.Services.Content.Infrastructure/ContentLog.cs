using Edvaniq.Services.Content.Domain;

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

    [LoggerMessage(EventId = 33910, Level = LogLevel.Warning, Message = "The AI is unavailable for the {Task} of document {DocumentId} ({Error}), outage {Outages}, asking again at {NextAttemptAt}")]
    public static partial void AiUnavailable(ILogger logger, string task, Guid documentId, string error, int outages, DateTime? nextAttemptAt);

    [LoggerMessage(EventId = 33911, Level = LogLevel.Error, Message = "The AI stayed unavailable for the {Task} of document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void AiStaysUnavailable(ILogger logger, string task, Guid documentId, int attempts);

    [LoggerMessage(EventId = 33920, Level = LogLevel.Warning, Message = "Planning could not say the profile of the learning goal of document {DocumentId}, it is marked as failed")]
    public static partial void GoalUnavailable(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 33921, Level = LogLevel.Warning, Message = "The learning goal of document {DocumentId} is gone, it is marked as failed")]
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

    [LoggerMessage(EventId = 33936, Level = LogLevel.Information, Message = "Document {DocumentId} needs {Generations} generations, its owner has {Left} left today, it is marked as failed")]
    public static partial void GenerationQuotaExceeded(ILogger logger, Guid documentId, int generations, int left);

    [LoggerMessage(Level = LogLevel.Information, Message = "Document {DocumentId} needs {Pages} pages, its owner has {Left} left today, it is marked as failed")]
    public static partial void PageQuotaExceeded(ILogger logger, Guid documentId, int pages, int left);

    [LoggerMessage(EventId = 33912, Level = LogLevel.Information, Message = "The AI budget is used up, the {Task} of document {DocumentId} is marked as failed")]
    public static partial void AiBudgetRefused(ILogger logger, string task, Guid documentId);

    [LoggerMessage(EventId = 33913, Level = LogLevel.Information, Message = "The AI is paused, the {Task} of document {DocumentId} is marked as failed")]
    public static partial void AiPaused(ILogger logger, string task, Guid documentId);

    // A wrong key or settings fail every document alike, this is the line for whoever runs the app.
    [LoggerMessage(EventId = 33914, Level = LogLevel.Error, Message = "The AI provider refused the {Task} of document {DocumentId} with status {Status} ({Error}), it is marked as failed. Check the key and the AI settings")]
    public static partial void AiRefused(ILogger logger, string task, Guid documentId, int? status, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI gave no usable topics for document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void AnalysisAnswerUnusable(ILogger logger, Guid documentId, int attempts);

    // What one AI call for a document cost, every attempt counted, failed or not. The rows in AiAttempts outlast the log,
    // the call id finds them. Topic and summary say what the call was about (#417), empty for the topics of a document.
    [LoggerMessage(EventId = 33915, Level = LogLevel.Information, Message = "AI usage for the {Task} of document {DocumentId}: call {CallId}, {Attempts} attempts, {InputTokens} input tokens, {OutputTokens} output tokens, cost {Cost} ({UnpricedAttempts} without price), {DurationMs} ms, prompt {PromptVersion}, model {Model}, topic {TopicId}, summary {SummaryId}")]
    public static partial void AiUsage(
        ILogger logger, string task, Guid documentId, Guid? topicId, Guid? summaryId, Guid callId, int attempts, long inputTokens,
        long outputTokens, decimal cost, int unpricedAttempts, long durationMs, string promptVersion, string model);

    // An answer the same user got before for the same request (#306), nothing asked and nothing paid.
    [LoggerMessage(EventId = 33916, Level = LogLevel.Information, Message = "AI usage for the {Task} of document {DocumentId}: reused, 0 attempts, 0 input tokens, 0 output tokens, cost 0, prompt {PromptVersion}, model {Model}, topic {TopicId}, summary {SummaryId}")]
    public static partial void AiUsageReused(
        ILogger logger, string task, Guid documentId, Guid? topicId, Guid? summaryId, string promptVersion, string model);

    [LoggerMessage(EventId = 33917, Level = LogLevel.Warning, Message = "A stored AI answer for document {DocumentId} could not be read, the AI is asked")]
    public static partial void AiAnswerNotRead(ILogger logger, Exception exception, Guid documentId);

    [LoggerMessage(EventId = 33918, Level = LogLevel.Information, Message = "The AI answer for document {DocumentId} was not stored for reuse")]
    public static partial void AiAnswerNotStored(ILogger logger, Exception exception, Guid documentId);

    [LoggerMessage(EventId = 33919, Level = LogLevel.Warning, Message = "The AI usage for the {Task} of document {DocumentId} could not be stored, the analysis goes on")]
    public static partial void AiUsageNotStored(ILogger logger, Exception exception, string task, Guid documentId);

    [LoggerMessage(EventId = 33932, Level = LogLevel.Error, Message = "The AI gave no valid question for a topic of document {DocumentId} in {Attempts} attempts, it is marked as failed")]
    public static partial void QuestionsAnswerUnusable(ILogger logger, Guid documentId, int attempts);

    [LoggerMessage(EventId = 33930, Level = LogLevel.Warning, Message = "Making question pools failed ({Error}), trying again in {Delay}")]
    public static partial void PoolRoundFailed(ILogger logger, string error, TimeSpan delay);

    [LoggerMessage(EventId = 33931, Level = LogLevel.Information, Message = "Document {DocumentId} is ready: {Topics} new question pools with {Questions} questions in {DurationMs} ms")]
    public static partial void PoolsMade(ILogger logger, Guid documentId, int topics, int questions, long durationMs);

    // Where a round broke off, without the exception's message: a parser or a provider may quote the material in it.
    public static string Describe(Exception exception) =>
        exception.TargetSite is { } site ? $"{exception.GetType().Name} in {site.DeclaringType?.Name}.{site.Name}" : exception.GetType().Name;

    [LoggerMessage(EventId = 33933, Level = LogLevel.Warning, Message = "Knowledge could not be asked for the topics of document {DocumentId} ({Error}), asking again")]
    public static partial void KnowledgeUnavailable(ILogger logger, Guid documentId, string error);

    [LoggerMessage(EventId = 33934, Level = LogLevel.Error, Message = "Knowledge has no topics of document {DocumentId} long after its analysis, it is marked as failed")]
    public static partial void TopicsMissing(ILogger logger, Guid documentId);

    [LoggerMessage(EventId = 33937, Level = LogLevel.Information, Message = "Document {DocumentId} was deleted during its {Step}, nothing of it was saved")]
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Reading process ended with {ExitCode} after {ElapsedMs} ms, answer {Bytes} bytes")]
    public static partial void FileReaderEnded(ILogger logger, int exitCode, int bytes, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading process killed after {ElapsedMs} ms, it took too long")]
    public static partial void FileReaderKilled(ILogger logger, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Read {Pages} pages and {Characters} characters from the file of document {DocumentId} in {DurationMs} ms")]
    public static partial void FileRead(ILogger logger, Guid documentId, int pages, int characters, long durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document {DocumentId} was still {Status} after {Timeout}, it is marked as failed")]
    public static partial void StuckDocumentFailed(ILogger logger, Guid documentId, DocumentStatus status, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The sweep marked {Count} documents as failed that took longer than {Timeout}")]
    public static partial void StuckDocumentsSwept(ILogger logger, int count, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking for stuck documents failed, trying again in {Delay}")]
    public static partial void SweepRoundFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(EventId = 33935, Level = LogLevel.Information, Message = "Topic {TopicId} has no material in document {DocumentId}, it gets no questions from it")]
    public static partial void TopicWithoutMaterial(ILogger logger, Guid topicId, Guid documentId);

    // Topping up question pools (#339). Fixed event ids, so the state of a pool can be found in the log.
    [LoggerMessage(EventId = 33900, Level = LogLevel.Warning, Message = "Topping up question pools failed ({Error}), trying again in {Delay}")]
    public static partial void TopUpRoundFailed(ILogger logger, string error, TimeSpan delay);

    [LoggerMessage(EventId = 33901, Level = LogLevel.Information, Message = "Question pool of topic {TopicId} got {Added} new questions and has {Questions} now, {Attempts} attempts")]
    public static partial void ToppedUp(ILogger logger, Guid topicId, int added, int questions, int attempts);

    [LoggerMessage(EventId = 33902, Level = LogLevel.Warning, Message = "Question pool of topic {TopicId} is not topped up, the AI is stopped by the operator: {Questions} questions, asking again at {NextTopUpAt}")]
    public static partial void TopUpPaused(ILogger logger, Guid topicId, int questions, DateTime nextTopUpAt);

    [LoggerMessage(EventId = 33903, Level = LogLevel.Warning, Message = "Question pool of topic {TopicId} is not topped up, the AI budget of the month is reached: {Questions} questions, asking again at {NextTopUpAt}")]
    public static partial void TopUpOverBudget(ILogger logger, Guid topicId, int questions, DateTime nextTopUpAt);

    [LoggerMessage(EventId = 33904, Level = LogLevel.Information, Message = "Question pool of topic {TopicId} is not topped up, the user's daily quota is used up: {Questions} questions, asking again at {NextTopUpAt}")]
    public static partial void TopUpOverQuota(ILogger logger, Guid topicId, int questions, DateTime nextTopUpAt);

    [LoggerMessage(EventId = 33905, Level = LogLevel.Warning, Message = "The AI is unavailable to top up the question pool of topic {TopicId} ({Error}), outage {Outages}, asking again at {NextTopUpAt}")]
    public static partial void TopUpPostponed(ILogger logger, Guid topicId, string error, int outages, DateTime? nextTopUpAt);

    [LoggerMessage(EventId = 33906, Level = LogLevel.Warning, Message = "Question pool of topic {TopicId} stays at {Questions} questions after {Rounds} top-up rounds")]
    public static partial void TopUpStayedShort(ILogger logger, Guid topicId, int questions, int rounds);

    [LoggerMessage(EventId = 33907, Level = LogLevel.Warning, Message = "Question pool of topic {TopicId} is no longer topped up and stays at {Questions} questions: {Reason}")]
    public static partial void TopUpStopped(ILogger logger, Guid topicId, int questions, string reason);

    [LoggerMessage(EventId = 33908, Level = LogLevel.Warning, Message = "Question pool of topic {TopicId} waits, Knowledge or Planning could not be asked ({Error}), asking again at {NextTopUpAt}")]
    public static partial void TopUpUnreachable(ILogger logger, Guid topicId, string error, DateTime nextTopUpAt);

    // New material of a topic with a pool (#915).
    [LoggerMessage(EventId = 33909, Level = LogLevel.Warning, Message = "Question pool of topic {TopicId} is full with {Questions} questions, it gets none on the new material of document {DocumentId}")]
    public static partial void PoolFull(ILogger logger, Guid topicId, Guid documentId, int questions);

    [LoggerMessage(EventId = 33960, Level = LogLevel.Information, Message = "Question pool of topic {TopicId} got {Added} new questions on {Sections} new sections of document {DocumentId}")]
    public static partial void PoolGotNewMaterial(ILogger logger, Guid topicId, Guid documentId, int sections, int added);

    // #298: codes only, never the text that was checked.
    [LoggerMessage(EventId = 33970, Level = LogLevel.Warning, Message = "Moderation flagged document {DocumentId}: {Codes}")]
    public static partial void ModerationFlagged(ILogger logger, Guid documentId, string codes);

    [LoggerMessage(EventId = 33971, Level = LogLevel.Warning, Message = "Moderation gave no readable answer for document {DocumentId} after {Attempts} attempts, nothing goes on")]
    public static partial void ModerationUnreadable(ILogger logger, Guid documentId, int attempts);
}