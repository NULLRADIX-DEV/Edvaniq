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

        var import = TopicImport.From(analysis, Goal, Now, []);

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
        var import = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, []), new("Genetik", parent, [])), Goal, Now, []);

        Assert.Null(import.Topics[1].ParentId);
    }

    [Fact]
    public void ParentThatIsASubtopic_HandsOverItsOwnTopic()
    {
        var import = TopicImport.From(
            Analysis(new TopicSuggestion("Zelle", null, []), new("Zellkern", 0, []), new("Chromatin", 1, [])),
            Goal,
            Now,
            []);

        Assert.Equal(import.Topics[0].Id, import.Topics[2].ParentId);
    }

    [Theory]
    [InlineData("  Zelle ", "Zelle")]
    [InlineData("   ", TopicImport.UnnamedTopic)]
    [InlineData("", TopicImport.UnnamedTopic)]
    public void Name_IsTrimmed_AndAnEmptyOneGetsAName(string name, string expected) =>
        Assert.Equal(expected, TopicImport.From(Analysis(new TopicSuggestion(name, null, [])), Goal, Now, []).Topics[0].Name);

    [Fact]
    public void TooLongName_IsCutTo80Characters()
    {
        var name = new string('a', 79) + " b" + new string('c', 10);

        var topic = TopicImport.From(Analysis(new TopicSuggestion(name, null, [])), Goal, Now, []).Topics[0];

        Assert.Equal(new string('a', 79), topic.Name);
        Assert.Null(Topic.CheckName(topic.Name));
    }

    [Fact]
    public void SectionNamedTwice_IsAssignedOnce()
    {
        var section = Guid.NewGuid();

        var import = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, [section, section])), Goal, Now, []);

        Assert.Single(import.Sections);
    }

    // #208: the same text analysed again, as a new document, brings no topic twice.
    [Fact]
    public void SameAnalysisAgain_TakesTheKnownTopics_AndMakesNoNewOne()
    {
        var first = TopicImport.From(Analysis(Cell()), Goal, Now, []);
        var again = Analysis(Cell());

        var import = TopicImport.From(again, Goal, Now.AddDays(1), first.Topics);

        Assert.Empty(import.Topics);
        Assert.Equal(
            first.Topics.SelectMany(topic => Enumerable.Repeat(topic.Id, topic.ParentId is null ? 1 : 2)),
            import.Sections.Select(section => section.TopicId));
        Assert.All(import.Sections, section => Assert.Equal(again.DocumentId, section.DocumentId));
    }

    [Fact]
    public void AnotherDocument_AddsItsNewTopics_AndHangsTheRestOnTheKnownOnes()
    {
        var first = TopicImport.From(Analysis(Cell()), Goal, Now, []);
        var (cell, nucleus) = (first.Topics[0], first.Topics[1]);
        var section = Guid.NewGuid();

        var import = TopicImport.From(
            Analysis(new TopicSuggestion("Zelle", null, [section]), new("Mitochondrien", 0, [Guid.NewGuid()]), new("Genetik", null, [Guid.NewGuid()])),
            Goal,
            Now,
            first.Topics);

        Assert.Equal(["Mitochondrien", "Genetik"], import.Topics.Select(topic => topic.Name));
        Assert.Equal([cell.Id, null], import.Topics.Select(topic => topic.ParentId));
        Assert.Equal((cell.Id, section), (import.Sections[0].TopicId, import.Sections[0].SectionId));
        Assert.Equal(("Zelle", "Zellkern"), (cell.Name, nucleus.Name));
    }

    [Theory]
    [InlineData("pH-Wert", "ph wert")]
    [InlineData("Größe", "groesse")]
    [InlineData("  Zell-Kern ", "ZELL KERN")]
    public void Names_MatchAfterNormalizing(string known, string suggested)
    {
        var topic = Topic.Create("alice", Goal, known, Now, Guid.NewGuid());

        var import = TopicImport.From(Analysis(new TopicSuggestion(suggested, null, [Guid.NewGuid()])), Goal, Now, [topic]);

        Assert.Empty(import.Topics);
        Assert.Equal(topic.Id, Assert.Single(import.Sections).TopicId);
        Assert.Equal(known.Trim(), topic.Name);
    }

    [Fact]
    public void RenamedTopic_IsFoundByTheNameTheAnalysisGaveIt_AndKeepsTheUsersName()
    {
        var topic = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, [])), Goal, Now, []).Topics[0];
        topic.Rename("Zellbiologie");

        var byOldName = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, [Guid.NewGuid()])), Goal, Now, [topic]);
        var byNewName = TopicImport.From(Analysis(new TopicSuggestion("Zellbiologie", null, [Guid.NewGuid()])), Goal, Now, [topic]);

        Assert.Empty(byOldName.Topics);
        Assert.Empty(byNewName.Topics);
        Assert.Equal([topic.Id, topic.Id], byOldName.Sections.Concat(byNewName.Sections).Select(section => section.TopicId));
        Assert.Equal("Zellbiologie", topic.Name);
    }

    // The same name on another level or under another topic is another topic.
    [Fact]
    public void SameName_OnAnotherLevelOrUnderAnotherTopic_IsANewTopic()
    {
        var known = TopicImport.From(Analysis(Cell()), Goal, Now, []).Topics;

        var import = TopicImport.From(
            Analysis(new TopicSuggestion("Zellkern", null, []), new("Physik", null, []), new("Zellkern", 1, [])),
            Goal,
            Now,
            known);

        Assert.Equal(["Zellkern", "Physik", "Zellkern"], import.Topics.Select(topic => topic.Name));
        Assert.Equal([null, null, import.Topics[1].Id], import.Topics.Select(topic => topic.ParentId));
    }

    [Fact]
    public void TopicsOfAnotherGoalOrUser_AreNeverTaken()
    {
        var otherGoal = Topic.Create("alice", Guid.NewGuid(), "Zelle", Now, Guid.NewGuid());
        var bobs = Topic.Create("bob", Goal, "Zelle", Now, Guid.NewGuid());

        var import = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, [])), Goal, Now, [otherGoal, bobs]);

        Assert.NotEqual(otherGoal.Id, Assert.Single(import.Topics).Id);
    }

    [Fact]
    public void EqualSuggestionsInOneAnalysis_BecomeOneTopic_WithTheSectionsOfBoth()
    {
        var sections = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var import = TopicImport.From(
            Analysis(new TopicSuggestion("Zelle", null, [sections[0]]), new("zelle", null, [sections[1], sections[0]])),
            Goal,
            Now,
            []);

        var topic = Assert.Single(import.Topics);
        Assert.Equal(sections, import.Sections.Select(section => section.SectionId));
        Assert.All(import.Sections, section => Assert.Equal(topic.Id, section.TopicId));
    }

    [Fact]
    public void SeveralKnownTopicsOfOneName_TheOldestTakesTheSections()
    {
        var younger = Topic.Create("alice", Goal, "Zelle", Now, Guid.NewGuid());
        var older = Topic.Create("alice", Goal, "Zelle", Now.AddDays(-1), Guid.NewGuid());

        var import = TopicImport.From(Analysis(new TopicSuggestion("Zelle", null, [Guid.NewGuid()])), Goal, Now, [younger, older]);

        Assert.Equal(older.Id, Assert.Single(import.Sections).TopicId);
    }

    private static TopicSuggestion[] Cell() =>
        [new("Zelle", null, [Guid.NewGuid()]), new("Zellkern", 0, [Guid.NewGuid(), Guid.NewGuid()])];

    private static DocumentAnalyzed Analysis(params TopicSuggestion[] topics) => new("alice", Guid.NewGuid(), topics);
}