using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.LearningEngine.Application;

// What LearningEngine takes from a session and gives for its result (#417). Fixed event ids (4172x), ids and numbers
// only, never the user or an answer.
public static partial class LearningEngineLog
{
    // Answers still missing this long after a session ended are more than late: the result waits for them (#406).
    public static readonly TimeSpan AnswersLateAfter = TimeSpan.FromMinutes(5);

    [LoggerMessage(EventId = 41720, EventName = "SessionIntroducedTopics", Level = LogLevel.Information,
        Message = "Session {SessionId} introduced {Count} topics")]
    public static partial void SessionIntroduced(ILogger logger, Guid sessionId, int count);

    [LoggerMessage(EventId = 41721, EventName = "IntroducedTopicNotKnownYet", Level = LogLevel.Information,
        Message = "Introduction of topic {TopicId} by session {SessionId} is kept, the topic isn't known yet; it counts once the topic is")]
    public static partial void IntroducedTopicNotKnownYet(ILogger logger, Guid topicId, Guid sessionId);

    [LoggerMessage(EventId = 41722, EventName = "MasteryChangesAnswersLate", Level = LogLevel.Warning,
        Message = "Mastery changes asked with {Missing} of {Answers} answers missing, {Minutes} min after the session ended")]
    public static partial void AnswersLate(ILogger logger, int missing, int answers, long minutes);

    // Logs when answers are missing longer than AnswersLateAfter after the end of the window. Nothing to remember for it,
    // the request says when the session ended.
    public static void IfAnswersLate(ILogger logger, int missing, int answers, DateTime ended, DateTime now)
    {
        if (missing > 0 && now - ended > AnswersLateAfter)
        {
            AnswersLate(logger, missing, answers, (long)(now - ended).TotalMinutes);
        }
    }
}