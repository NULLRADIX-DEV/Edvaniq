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

    [LoggerMessage(Level = LogLevel.Error, Message = "The AI gave no usable topics for document {DocumentId}, it is marked as failed")]
    public static partial void AnalysisAnswerUnusable(ILogger logger, Guid documentId);
}
