using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Planning.Application;

// The session flow (#417). Fixed event ids (417xx), so a session's way can be found in the log, its TraceId comes with
// the scope. Ids, numbers and outcomes only, never the user, a topic's name or an answer.
public static partial class PlanningLog
{
    [LoggerMessage(EventId = 41700, EventName = "SessionStarted", Level = LogLevel.Information,
        Message = "Session {SessionId} started on block {BlockId} ({Kind}, {Minutes} min)")]
    public static partial void SessionStarted(ILogger logger, Guid sessionId, Guid blockId, string kind, int minutes);

    [LoggerMessage(EventId = 41701, EventName = "SessionGoesOn", Level = LogLevel.Information,
        Message = "Session {SessionId} is still running, a start brought it back")]
    public static partial void SessionGoesOn(ILogger logger, Guid sessionId);

    [LoggerMessage(EventId = 41702, EventName = "SessionBlockGone", Level = LogLevel.Information,
        Message = "No session started, block {BlockId} is not in today's plan any more")]
    public static partial void BlockGone(ILogger logger, Guid blockId);

    [LoggerMessage(EventId = 41703, EventName = "SessionBlockDone", Level = LogLevel.Information,
        Message = "No session started, block {BlockId} is done already")]
    public static partial void BlockDone(ILogger logger, Guid blockId);

    [LoggerMessage(EventId = 41704, EventName = "SessionEnded", Level = LogLevel.Information,
        Message = "Session {SessionId} ended {Outcome} after {DurationSeconds} s, {ActiveSeconds} s of active learning")]
    public static partial void SessionEnded(ILogger logger, Guid sessionId, string outcome, long durationSeconds, int activeSeconds);

    [LoggerMessage(EventId = 41705, EventName = "SessionEndedAlready", Level = LogLevel.Information,
        Message = "Session {SessionId} was {Outcome} already, nothing sent again")]
    public static partial void SessionEndedAlready(ILogger logger, Guid sessionId, string outcome);

    [LoggerMessage(EventId = 41706, EventName = "SessionChangeConflict", Level = LogLevel.Information,
        Message = "Session {SessionId} was changed at the same time, trying again (attempt {Attempt})")]
    public static partial void ChangeConflict(ILogger logger, Guid sessionId, int attempt);

    [LoggerMessage(EventId = 41707, EventName = "SessionChangeGaveUp", Level = LogLevel.Error,
        Message = "Session {SessionId} kept changing, a change was not stored in {Attempts} attempts")]
    public static partial void ChangeGaveUp(ILogger logger, Guid sessionId, int attempts);

    [LoggerMessage(EventId = 41708, EventName = "SessionResultWithoutAnswers", Level = LogLevel.Warning,
        Message = "Result of session {SessionId} is unavailable, Assessment could not be asked for its answers")]
    public static partial void ResultWithoutAnswers(ILogger logger, Guid sessionId);

    [LoggerMessage(EventId = 41709, EventName = "SessionResultWithoutMastery", Level = LogLevel.Warning,
        Message = "Result of session {SessionId} comes without mastery, LearningEngine could not be asked")]
    public static partial void ResultWithoutMastery(ILogger logger, Guid sessionId);

    [LoggerMessage(EventId = 41714, EventName = "BonusReviewStarted", Level = LogLevel.Information,
        Message = "Bonus review {SessionId} started as block {BlockId} ({Minutes} min)")]
    public static partial void BonusStarted(ILogger logger, Guid sessionId, Guid blockId, int minutes);

    [LoggerMessage(EventId = 41715, EventName = "BonusReviewWithoutTopic", Level = LogLevel.Information,
        Message = "No bonus review started, no introduced topic has a ready question pool")]
    public static partial void BonusWithoutTopic(ILogger logger);

    [LoggerMessage(EventId = 41716, EventName = "BonusReviewWithoutMastery", Level = LogLevel.Warning,
        Message = "No bonus review started, LearningEngine could not be asked for the mastery")]
    public static partial void BonusWithoutMastery(ILogger logger);

    // Event ids of the queries to other services, each logs where it fails.
    public static class Queries
    {
        public const int MasteryUnavailable = 41710;

        public const int MasteryChangesUnavailable = 41711;

        public const int AnswersUnavailable = 41712;

        public const int QuestionPoolsUnavailable = 41713;
    }
}