namespace Edvaniq.Client.Core;

// The user's learning goals in Planning (docs/spec/glossary.md#lernziel).
public interface ILearningGoals
{
    // The goals the user created, oldest first.
    Task<CallOutcome<IReadOnlyList<LearningGoalInfo>>> ListAsync(CancellationToken cancellationToken = default);

    // Rejected names what is wrong per field (FieldErrors, keys from LearningGoalValues.Fields).
    Task<CallOutcome<LearningGoalInfo>> CreateAsync(NewLearningGoal goal, CancellationToken cancellationToken = default);
}

// The values as Planning takes them. ExamDate as yyyy-MM-dd.
public sealed record NewLearningGoal(
    string Kind,
    string Name,
    string? ExamType,
    string? ExamDate,
    string? Level,
    string OutputLanguage,
    string? Goal);

// Kind, exam type and level as strings, so a value Planning adds later shows as unknown instead of breaking the list.
public sealed record LearningGoalInfo(
    Guid Id,
    string Kind,
    string Name,
    string? ExamType,
    DateOnly? ExamDate,
    string? Level,
    string OutputLanguage,
    string? Goal,
    DateTime CreatedAt);

// The values and limits of LearningGoalFields in Planning. The client can't reference Planning, so
// LearningGoalValuesTests in the architecture tests holds both sides together.
public static class LearningGoalValues
{
    public const string ExamPreparation = "ExamPreparation";

    public const string FreeLearning = "FreeLearning";

    public const int NameMaxLength = 80;

    public const int GoalMaxLength = 200;

    public const string DefaultOutputLanguage = "de";

    public static readonly IReadOnlyList<string> Kinds = [ExamPreparation, FreeLearning];

    public static readonly IReadOnlyList<string> ExamTypes = ["Ex", "Schulaufgabe", "Klausur", "Ihk", "Other"];

    public static readonly IReadOnlyList<string> Levels =
        ["LowerSecondary", "MiddleSecondary", "UpperSecondary", "Apprenticeship", "University", "SelfLearner"];

    public static readonly IReadOnlyList<string> OutputLanguages = [DefaultOutputLanguage, "en"];

    // The field names Planning uses in a validation problem.
    public static class Fields
    {
        public const string Kind = "Kind";

        public const string Name = "Name";

        public const string ExamType = "ExamType";

        public const string ExamDate = "ExamDate";

        public const string Level = "Level";

        public const string OutputLanguage = "OutputLanguage";

        public const string Goal = "Goal";

        public static readonly IReadOnlyList<string> All = [Kind, Name, ExamType, ExamDate, Level, OutputLanguage, Goal];
    }
}