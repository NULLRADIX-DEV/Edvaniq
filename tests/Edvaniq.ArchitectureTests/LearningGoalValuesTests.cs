using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.ArchitectureTests;

// #273: the assistant offers exactly the values Planning takes and checks with Planning's limits and words. The client
// can't reference Planning, so a change on one side turns this red.
public class LearningGoalValuesTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Values_AreThoseOfPlanning()
    {
        Assert.Equal(Enum.GetNames<LearningGoalKind>(), LearningGoalValues.Kinds);
        Assert.Equal(nameof(LearningGoalKind.ExamPreparation), LearningGoalValues.ExamPreparation);
        Assert.Equal(nameof(LearningGoalKind.FreeLearning), LearningGoalValues.FreeLearning);
        Assert.Equal(Enum.GetNames<ExamType>(), LearningGoalValues.ExamTypes);
        Assert.Equal(Enum.GetNames<LearningLevel>(), LearningGoalValues.Levels);
        Assert.Equal(LearningGoalFields.OutputLanguages, LearningGoalValues.OutputLanguages);
        Assert.Equal(LearningGoalFields.DefaultOutputLanguage, LearningGoalValues.DefaultOutputLanguage);
        Assert.Equal(LearningGoalFields.NameMaxLength, LearningGoalValues.NameMaxLength);
        Assert.Equal(LearningGoalFields.GoalMaxLength, LearningGoalValues.GoalMaxLength);
    }

    // The keys of Planning's validation problem are the names of LearningGoalFields' properties.
    [Fact]
    public void Fields_AreThoseOfPlanning() =>
        Assert.Equal(
            typeof(LearningGoalFields).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal),
            LearningGoalValues.Fields.All.Order(StringComparer.Ordinal));

    [Fact]
    public void EveryValue_HasAText()
    {
        Assert.Equal(LearningGoalValues.ExamTypes, FeedbackTexts.Goal.ExamTypes.Keys);
        Assert.Equal(LearningGoalValues.Levels, FeedbackTexts.Goal.Levels.Keys);
        Assert.Equal(LearningGoalValues.OutputLanguages, FeedbackTexts.Goal.OutputLanguages.Keys);
    }

    // The assistant checks these before it goes on, with the same words Planning would answer.
    [Fact]
    public void ClientErrors_AreThoseOfPlanning()
    {
        Assert.Equal(FeedbackTexts.Goal.KindMissing, ErrorOf(nameof(LearningGoalFields.Kind), kind: null));
        Assert.Equal(FeedbackTexts.Goal.NameMissing, ErrorOf(nameof(LearningGoalFields.Name), name: " "));
        Assert.Equal(FeedbackTexts.Goal.NameTooLong, ErrorOf(nameof(LearningGoalFields.Name), name: new string('a', LearningGoalFields.NameMaxLength + 1)));
        Assert.Equal(FeedbackTexts.Goal.ExamTypeMissing, ErrorOf(nameof(LearningGoalFields.ExamType), examType: null));
        Assert.Equal(FeedbackTexts.Goal.GoalTooLong, ErrorOf(nameof(LearningGoalFields.Goal), goal: new string('a', LearningGoalFields.GoalMaxLength + 1)));
    }

    private static string ErrorOf(
        string field,
        string? kind = nameof(LearningGoalKind.ExamPreparation),
        string? name = "Bio",
        string? examType = nameof(ExamType.Klausur),
        string? goal = null)
    {
        Assert.Null(LearningGoalFields.Parse(kind, name, examType, null, null, null, goal, Now, out var errors));
        return Assert.Single(errors[field]);
    }
}