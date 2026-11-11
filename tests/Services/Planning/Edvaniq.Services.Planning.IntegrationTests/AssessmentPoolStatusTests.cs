using Edvaniq.Services.Planning.Infrastructure;

namespace Edvaniq.Services.Planning.IntegrationTests;

// The pool status Assessment puts on the wire (docs/architecture.md, Fragenpool), read in exactly one place. Only
// "ready" is ready: Planning never counts questions itself, the rule stays in Assessment.
public class AssessmentPoolStatusTests
{
    [Fact]
    public void Ready_IsReady() => Assert.True(AssessmentQuestionPools.IsReady("ready"));

    [Theory]
    [InlineData("notReady")]
    [InlineData("filling")]
    [InlineData("Ready")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherOrUnknownStatus_IsNotReady(string? status) =>
        Assert.False(AssessmentQuestionPools.IsReady(status));
}