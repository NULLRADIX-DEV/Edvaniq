using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.UnitTests;

// What DocumentAnalyzed turns into: two levels in the order of the analysis, the sections with them, and whatever
// doesn't fit fixed up instead of refused.
public class TopicImportTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Goal = Guid.NewGuid();

    [Fact]
    public void Suggestions_BecomeTopicsInTwoLevels_InTheirOrder_WithTheirSections()
    {
        var sections = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var analysis = Analysis(
            new("Zelle", null, [sections[0]]),
            new("Zellkern", 0, [sections[1], sections[2]]),
            new("Genetik", null, [sections[3]]));

        var import = TopicImport.From(analysis, Goal, Now);

        var (cell, nucleus, genetics) = (import.Topics[0], import.Topics[1], import.Topics[2]);
        Assert.Equal(["Zelle", "Zellkern", "Genetik"], import.Topics.Select(topic => topic.Name));
        Assert.Equal([0, 1, 2], import.Topics.Select(topic => topic.Position));
        Assert.Equal([null, cell.Id, null], import.Topics.Select(topic => topic.ParentId));
        Assert.All(import.Topics, topic => Assert.Equal(
            ("alice", Goal, analysis.DocumentId, Now),
            (topic.OwnerId, topic.LearningGoalId, topic.DocumentId, topic.CreatedAt)));
        Assert.Equal(
            [(cell.Id, sections[0]), (nucleus.Id, sections[1]), (nucleus.Id, sections[2]), (genetics.Id, sections[3])],
            import.Sections.Select(section => (section.TopicId, section.SectionId)));
        Assert.All(import.Sections, section => Assert.Equal("alice", section.OwnerId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    public void ParentThatIsNoEarlierSuggestion_MakesATopicOfTheUpperLevel(int parent)
    {
        var import = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, []), new("Genetik", parent, [])), Goal, Now);

        Assert.Null(import.Topics[1].ParentId);
    }

    [Fact]
    public void ParentThatIsASubtopic_HandsOverItsOwnTopic()
    {
        var import = TopicImport.From(
            Analysis(new TopicSuggestion("Zelle", null, []), new("Zellkern", 0, []), new("Chromatin", 1, [])),
            Goal,
            Now);

        Assert.Equal(import.Topics[0].Id, import.Topics[2].ParentId);
    }

    [Theory]
    [InlineData("  Zelle ", "Zelle")]
    [InlineData("   ", TopicImport.UnnamedTopic)]
    [InlineData("", TopicImport.UnnamedTopic)]
    public void Name_IsTrimmed_AndAnEmptyOneGetsAName(string name, string expected) =>
        Assert.Equal(expected, TopicImport.From(Analysis(new TopicSuggestion(name, null, [])), Goal, Now).Topics[0].Name);

    [Fact]
    public void TooLongName_IsCutTo80Characters()
    {
        var name = new string('a', 79) + " b" + new string('c', 10);

        var topic = TopicImport.From(Analysis(new TopicSuggestion(name, null, [])), Goal, Now).Topics[0];

        Assert.Equal(new string('a', 79), topic.Name);
        Assert.Null(Topic.CheckName(topic.Name));
    }

    [Fact]
    public void SectionNamedTwice_IsAssignedOnce()
    {
        var section = Guid.NewGuid();

        var import = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, [section, section])), Goal, Now);

        Assert.Single(import.Sections);
    }

    private static DocumentAnalyzed Analysis(params TopicSuggestion[] topics) => new("alice", Guid.NewGuid(), topics);
}