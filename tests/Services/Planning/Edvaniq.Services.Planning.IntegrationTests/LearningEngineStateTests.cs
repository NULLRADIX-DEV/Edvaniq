using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;

namespace Edvaniq.Services.Planning.IntegrationTests;

// The state names LearningEngine puts on the wire (docs/architecture.md, rule 6), read in exactly one place.
public class LearningEngineStateTests
{
    [Theory]
    [InlineData("new", TopicState.New)]
    [InlineData("weakness", TopicState.Weakness)]
    [InlineData("building", TopicState.Building)]
    [InlineData("secure", TopicState.Secure)]
    public void EveryStateName_IsRead(string name, TopicState state) =>
        Assert.Equal(state, LearningEngineMastery.StateOf(name));

    [Theory]
    [InlineData("forgotten")]
    [InlineData("New")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownOrMissingName_GivesNoState(string? name) =>
        Assert.Null(LearningEngineMastery.StateOf(name));

    [Fact]
    public void EveryStateOfPlanning_HasAName() =>
        Assert.All(Enum.GetValues<TopicState>(), state =>
            Assert.Equal(state, LearningEngineMastery.StateOf(state.ToString().ToLowerInvariant())));
}