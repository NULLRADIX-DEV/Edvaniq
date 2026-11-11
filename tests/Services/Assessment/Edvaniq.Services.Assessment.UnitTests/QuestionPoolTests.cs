using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.UnitTests;

public sealed class QuestionPoolTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(20, true)]
    public void ReadyFromTwoQuestions(int questions, bool ready) =>
        Assert.Equal(ready, QuestionPool.IsReady(questions));

    // The check questions and the status follow the same rule: a pool is ready exactly when a check can be picked.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReadyExactlyWhenTheCheckCanBePicked(int count) =>
        Assert.Equal(
            QuestionPool.IsReady(count),
            CheckQuestions.Pick(CheckQuestionsTests.Pool([.. Enumerable.Repeat(1, count)]), new Random(7)) is not null);
}