using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// A pasted text becomes a document only with content and within the limits, and the client shows the reason otherwise.
public class DocumentTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Goal = Guid.NewGuid();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    public void EmptyText_IsRejected(string? text) =>
        Assert.Equal("Der Text ist leer. Füge den Text ein, aus dem du lernen willst.", Document.CheckText(text));

    [Fact]
    public void TextAtTheLimit_IsAccepted() =>
        Assert.Null(Document.CheckText(string.Concat(Enumerable.Repeat("Wort ", Document.TextMaxLength / 5))));

    [Theory]
    [InlineData("- 1 -\n\n  .   ,    '   .\n |  .  _   ,   ~  .\n\n- 2 -")]
    [InlineData("12 34 56 78 90 12 34 56 78 90 = + - * / % 3,14")]
    [InlineData("ab cd ef gh ij kl mn op qr st uv wx yz")]
    [InlineData("Licht spaltet Wasser, dabei entsteht Sauerstoff und")]
    public void TextWithTooFewRealWords_IsRejectedWithTheNextStep(string text) =>
        Assert.Equal(
            "Der Text enthält zu wenig lesbaren Inhalt, etwa weil er aus einem Scan stammt. Füge einen Text mit ganzen Sätzen ein, zum Beispiel aus einer Textversion des Materials.",
            Document.CheckText(text));

    [Theory]
    [InlineData("Licht spaltet Wasser, dabei entsteht Sauerstoff und Zucker")]
    [InlineData("Ein Atom ist der kleinste Baustein eines Elements. Es besteht aus Kern und Hülle. Im Kern liegen Protonen und Neutronen.")]
    [InlineData("Säure: CO2, H2O, pH 7, NaCl, HCl, H2SO4, OH-, H3O+, c = n/V, pH = -lg c(H3O+).\nEine Säure gibt Protonen ab. Eine Base nimmt sie auf.")]
    [InlineData("Größe Übung Ärger Öl-Spur Maß Straße Süße Tür")]
    public void ShortTextWithRealWords_IsAccepted(string text) =>
        Assert.Null(Document.CheckText(text));

    [Fact]
    public void TextOverTheLimit_IsRejected() =>
        Assert.Equal(
            "Der Text ist zu lang. Erlaubt sind höchstens 200.000 Zeichen.",
            Document.CheckText(new string('a', Document.TextMaxLength + 1)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Photosynthese")]
    public void MissingOrShortTitle_IsAccepted(string? title) =>
        Assert.Null(Document.CheckTitle(title));

    [Fact]
    public void TitleAtTheLimit_CountsWithoutTheSpacesAround() =>
        Assert.Null(Document.CheckTitle($"  {new string('a', Document.TitleMaxLength)}  "));

    [Fact]
    public void TitleOverTheLimit_IsRejected() =>
        Assert.Equal(
            "Der Titel ist zu lang. Erlaubt sind höchstens 80 Zeichen.",
            Document.CheckTitle(new string('a', Document.TitleMaxLength + 1)));

    [Fact]
    public void Paste_KeepsTheTextAsItIs_AndStartsAsUploaded()
    {
        const string text = "\r\n  Photosynthese \r\nLicht wird zu Zucker.  ";

        var document = Document.Paste("alice", Goal, "  Biologie  ", text, Now);

        Assert.Equal(("alice", Goal), (document.OwnerId, document.LearningGoalId));
        Assert.Equal("Biologie", document.Title);
        Assert.Equal(text, document.Text);
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
        Assert.Equal(Now, document.CreatedAt);
        Assert.NotEqual(Guid.Empty, document.Id);
    }

    [Fact]
    public void Paste_KeepsLevelAndOutputLanguageOfTheGoal()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now, "University", "en");

        Assert.Equal(("University", "en"), (document.Level, document.OutputLanguage));
    }

    // A cut name would read as another value, so one that is too long counts as none.
    [Fact]
    public void Paste_DropsALevelOrLanguageThatDoesntFit()
    {
        var document = Document.Paste(
            "alice", Goal, null, "Licht spaltet Wasser.", Now,
            new string('a', Document.LevelMaxLength + 1), new string('e', Document.OutputLanguageMaxLength + 1));

        Assert.Equal((null, null), (document.Level, document.OutputLanguage));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void PasteWithoutTitle_TakesTheFirstLineWithContent(string? title) =>
        Assert.Equal("Photosynthese", Document.Paste("alice", Goal, title, "\r\n  \r\n  Photosynthese \r\nLicht wird zu Zucker.", Now).Title);

    [Fact]
    public void Split_GivesSectionsOfTheDocument_AndStartsTheAnalysis()
    {
        var document = Document.Paste("alice", Goal, null, "# Photosynthese\nLicht spaltet Wasser.\n\n# Zellatmung\nZucker wird zu Energie.", Now);

        var sections = document.Split();

        Assert.Equal(DocumentStatus.Analyzing, document.Status);
        Assert.Null(document.FailureReason);
        Assert.Equal([(0, 0, 37), (1, 39, 36)], sections.Select(section => (section.Ordinal, section.Start, section.Length)));
        Assert.All(sections, section => Assert.Equal((document.Id, "alice"), (section.DocumentId, section.OwnerId)));
    }

    [Fact]
    public void Split_OfWhiteSpace_FailsTheDocumentWithAReason()
    {
        var document = Document.Paste("alice", Goal, "Leer", " \r\n ", Now);

        Assert.Empty(document.Split());
        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("Der Text enthält nichts, woraus sich lernen lässt. Füge einen anderen Text ein.", document.FailureReason);
    }

    [Fact]
    public void Split_Twice_IsRefused()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        document.Split();

        Assert.Throws<InvalidOperationException>(() => document.Split());
    }

    // The topics are out, the questions are still to come: the document stays in analysis.
    [Fact]
    public void ReportTopics_KeepsTheDocumentInAnalysis_UntilItsQuestionsAreThere()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        document.Split();

        document.ReportTopics(Now.AddMinutes(1));

        Assert.Equal((DocumentStatus.Analyzing, Now.AddMinutes(1), true), (document.Status, document.AnalyzedAt, document.WaitsForQuestions));
        document.FinishAnalysis();
        Assert.Equal((DocumentStatus.Ready, false), (document.Status, document.WaitsForQuestions));
    }

    [Fact]
    public void ReportTopics_Twice_IsRefused()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        document.Split();
        document.ReportTopics(Now);

        Assert.Throws<InvalidOperationException>(() => document.ReportTopics(Now));
    }

    [Fact]
    public void FinishAnalysis_BeforeTheTopicsAreReported_IsRefused()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        Assert.Throws<InvalidOperationException>(document.FinishAnalysis);

        document.Split();
        Assert.Throws<InvalidOperationException>(document.FinishAnalysis);
    }

    [Fact]
    public void Fail_CutsALongReason()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);

        document.Fail(new string('x', Document.FailureReasonMaxLength + 1));

        Assert.Equal(Document.FailureReasonMaxLength, document.FailureReason?.Length);
    }

    // No topic went out yet, so the retry starts over from splitting.
    [Fact]
    public void Retry_BeforeTheTopicsWentOut_StartsOverFromSplitting()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        document.Split();
        document.Fail(Document.AnalysisFailed);

        document.Retry(Now.AddMinutes(5));

        Assert.Equal((DocumentStatus.Uploaded, (string?)null, (DateTime?)Now.AddMinutes(5)), (document.Status, document.FailureReason, document.RetriedAt));
        Assert.NotEmpty(document.Split());
    }

    // The topics are out and keep their ids at Knowledge, only the questions follow.
    [Fact]
    public void Retry_AfterTheTopicsWentOut_WaitsForTheQuestionsAgain()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        document.Split();
        document.ReportTopics(Now.AddMinutes(1));
        document.Fail(Document.QuestionsFailed);

        document.Retry(Now.AddMinutes(30));

        Assert.Equal((DocumentStatus.Analyzing, true, (DateTime?)Now.AddMinutes(1), (string?)null),
            (document.Status, document.WaitsForQuestions, document.AnalyzedAt, document.FailureReason));
        Assert.Equal(Now.AddMinutes(30), document.WaitsForTopicsSince);
    }

    [Fact]
    public void WaitsForTopicsSince_WithoutRetry_IsTheAnalysis()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        document.Split();
        document.ReportTopics(Now.AddMinutes(1));

        Assert.Equal(Now.AddMinutes(1), document.WaitsForTopicsSince);
    }

    [Fact]
    public void Retry_OfADocumentThatIsNotFailed_IsRefused()
    {
        var document = Document.Paste("alice", Goal, null, "Licht spaltet Wasser.", Now);
        Assert.Throws<InvalidOperationException>(() => document.Retry(Now));

        document.Split();
        Assert.Throws<InvalidOperationException>(() => document.Retry(Now));
    }

    [Fact]
    public void PasteWithoutTitle_CutsALongFirstLine()
    {
        var line = new string('a', Document.TitleMaxLength - 1) + " und noch viel mehr";

        Assert.Equal(new string('a', Document.TitleMaxLength - 1), Document.Paste("alice", Goal, null, line, Now).Title);
    }
}