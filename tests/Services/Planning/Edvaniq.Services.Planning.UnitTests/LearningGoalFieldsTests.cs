using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.UnitTests;

public sealed class LearningGoalFieldsTests
{
    // 11.11.2026 12:00 in Berlin.
    private static readonly DateTime Noon = new(2026, 11, 11, 11, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ExamPreparation_WithAllFields_IsTaken()
    {
        var fields = Parse("ExamPreparation", "  Mathe Schulaufgabe  ", "Schulaufgabe", "2026-12-01", "MiddleSecondary", "en", "  Eine 2 schaffen ");

        Assert.Equal(
            new LearningGoalFields(
                LearningGoalKind.ExamPreparation,
                "Mathe Schulaufgabe",
                ExamType.Schulaufgabe,
                new DateOnly(2026, 12, 1),
                LearningLevel.MiddleSecondary,
                "en",
                "Eine 2 schaffen"),
            fields);
    }

    [Fact]
    public void FreeLearning_NeedsOnlyKindAndName()
    {
        var fields = Parse("FreeLearning", "C#", null, null, null, null, null);

        Assert.Equal(new LearningGoalFields(LearningGoalKind.FreeLearning, "C#", null, null, null, "de", null), fields);
    }

    [Fact]
    public void ExamPreparation_WithoutDate_PlansRolling()
    {
        var fields = Parse("ExamPreparation", "IHK", "Ihk", " ", null, null, null);

        Assert.NotNull(fields);
        Assert.Null(fields.ExamDate);
    }

    [Fact]
    public void Nothing_GetsAMessageForKindAndName()
    {
        var errors = Errors(null, "   ", null, null, null, null, null);

        Assert.Equal(["Kind", "Name"], errors.Keys.Order());
        Assert.Equal("Gib dem Lernziel einen Namen.", errors["Name"].Single());
    }

    [Fact]
    public void ExamPreparation_WithoutExamType_IsRejected() =>
        Assert.Equal(["ExamType"], Errors("ExamPreparation", "Mathe", null, null, null, null, null).Keys);

    [Fact]
    public void FreeLearning_WithExamTypeOrDate_IsRejected() =>
        Assert.Equal(["ExamDate", "ExamType"], Errors("FreeLearning", "C#", "Klausur", "2026-12-01", null, null, null).Keys.Order());

    [Theory]
    [InlineData("exampreparation")]
    [InlineData("0")]
    [InlineData("Exam")]
    public void UnknownKind_IsRejected(string kind) =>
        Assert.Equal(["Kind"], Errors(kind, "Mathe", null, null, null, null, null).Keys);

    [Theory]
    [InlineData("Level", "Grundschule")]
    [InlineData("OutputLanguage", "fr")]
    [InlineData("ExamType", "Abitur")]
    public void UnknownChoice_IsRejectedAtItsField(string field, string value)
    {
        var errors = Errors(
            "ExamPreparation",
            "Mathe",
            field == "ExamType" ? value : "Klausur",
            null,
            field == "Level" ? value : null,
            field == "OutputLanguage" ? value : null,
            null);

        Assert.Equal([field], errors.Keys);
    }

    [Fact]
    public void TooLongNameAndGoal_AreRejected()
    {
        var errors = Errors("FreeLearning", new string('a', 81), null, null, null, null, new string('b', 201));

        Assert.Equal(["Goal", "Name"], errors.Keys.Order());
    }

    [Fact]
    public void LongestNameAndGoal_AreTaken() =>
        Assert.NotNull(Parse("FreeLearning", new string('a', 80), null, null, null, null, new string('b', 200)));

    [Theory]
    [InlineData("2026-13-01")]
    [InlineData("01.12.2026")]
    public void BrokenDate_IsRejected(string date) =>
        Assert.Equal("Das Datum ist ungültig.", Errors("ExamPreparation", "Mathe", "Ex", date, null, null, null)["ExamDate"].Single());

    [Fact]
    public void Yesterday_IsInThePast() =>
        Assert.Equal(
            "Der Prüfungstermin darf nicht in der Vergangenheit liegen.",
            Errors("ExamPreparation", "Mathe", "Ex", "2026-11-10", null, null, null)["ExamDate"].Single());

    [Fact]
    public void Today_IsAllowed() =>
        Assert.NotNull(Parse("ExamPreparation", "Mathe", "Ex", "2026-11-11", null, null, null));

    // Today means the study day: at 03:59 in Berlin it is still the day before, so that day is still allowed.
    [Fact]
    public void BeforeFourInTheMorning_TheDayBeforeIsStillToday()
    {
        var earlyMorning = new DateTime(2026, 11, 11, 2, 59, 0, DateTimeKind.Utc);
        var atFour = new DateTime(2026, 11, 11, 3, 0, 0, DateTimeKind.Utc);

        Assert.NotNull(Parse("ExamPreparation", "Mathe", "Ex", "2026-11-10", null, null, null, earlyMorning));
        Assert.Null(Parse("ExamPreparation", "Mathe", "Ex", "2026-11-10", null, null, null, atFour));
    }

    private static LearningGoalFields? Parse(
        string? kind, string? name, string? examType, string? examDate, string? level, string? language, string? goal, DateTime? now = null) =>
        LearningGoalFields.Parse(kind, name, examType, examDate, level, language, goal, now ?? Noon, out _);

    private static Dictionary<string, string[]> Errors(
        string? kind, string? name, string? examType, string? examDate, string? level, string? language, string? goal)
    {
        Assert.Null(LearningGoalFields.Parse(kind, name, examType, examDate, level, language, goal, Noon, out var errors));
        return errors;
    }
}