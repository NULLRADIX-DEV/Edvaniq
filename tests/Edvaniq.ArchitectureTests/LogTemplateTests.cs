namespace Edvaniq.ArchitectureTests;

// #417: log templates name no user and carry nothing a user wrote.
public class LogTemplateTests
{
    [Fact]
    public void LogTemplates_NameNoUserAndNoContent() =>
        Rules.AssertNone(LogTemplateRule.Violations(Repository.Root), "The log rule");

    // The check itself: a template over two lines, with a forbidden placeholder among allowed ones, is found, one of ids
    // only and a comment are not.
    [Fact]
    public void Check_FindsAForbiddenPlaceholder()
    {
        const string source = """
            // [LoggerMessage(Level = LogLevel.Information, Message = "Old {UserId}")]
            [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Session {SessionId} is fine")]
            public static partial void Fine(ILogger logger, Guid sessionId);

            [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
                Message = "Session {SessionId} of {UserId} said {answer} in {Count:N0}")]
            public static partial void Bad(ILogger logger, Guid sessionId, string userId, string answer, int count);
            """;

        Assert.Equal(["Log.cs:5 logs {UserId}", "Log.cs:5 logs {answer}"], LogTemplateRule.Check("Log.cs", source));
    }
}