using System.Globalization;

using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Planning.Domain;

// docs/spec/glossary.md#art-des-lernziels
public enum LearningGoalKind
{
    ExamPreparation,
    FreeLearning,
}

// Only a label, nothing in the planner depends on it. The values are German school terms without a clean English match.
public enum ExamType
{
    Ex,
    Schulaufgabe,
    Klausur,
    Ihk,
    Other,
}

public enum LearningLevel
{
    LowerSecondary,
    MiddleSecondary,
    UpperSecondary,
    Apprenticeship,
    University,
    SelfLearner,
}

// Everything a user enters for a learning goal, checked. Parse takes the raw strings of a request, so an unknown value
// gets a message at its field instead of a binding error.
public sealed record LearningGoalFields(
    LearningGoalKind Kind,
    string Name,
    ExamType? ExamType,
    DateOnly? ExamDate,
    LearningLevel? Level,
    string OutputLanguage,
    string? Goal)
{
    public const int NameMaxLength = 80;

    public const int GoalMaxLength = 200;

    public const string DefaultOutputLanguage = "de";

    // The spec names no languages. German is the default, English the one other language the app is built to write in.
    public static readonly IReadOnlyList<string> OutputLanguages = [DefaultOutputLanguage, "en"];

    public static LearningGoalFields? Parse(
        string? kind,
        string? name,
        string? examType,
        string? examDate,
        string? level,
        string? outputLanguage,
        string? goal,
        DateTime utcNow,
        out Dictionary<string, string[]> errors)
    {
        errors = [];

        var parsedKind = ParseEnum<LearningGoalKind>(kind);
        if (parsedKind is null)
        {
            errors[nameof(Kind)] = ["Wähle, ob du auf eine Prüfung lernst oder frei lernst."];
        }

        var trimmedName = name?.Trim() ?? "";
        if (trimmedName.Length == 0)
        {
            errors[nameof(Name)] = ["Gib dem Lernziel einen Namen."];
        }
        else if (trimmedName.Length > NameMaxLength)
        {
            errors[nameof(Name)] = [$"Der Name darf höchstens {NameMaxLength} Zeichen lang sein."];
        }

        ExamType? parsedExamType = null;
        DateOnly? parsedExamDate = null;
        if (parsedKind == LearningGoalKind.ExamPreparation)
        {
            parsedExamType = ParseEnum<ExamType>(examType);
            if (parsedExamType is null)
            {
                errors[nameof(ExamType)] = ["Wähle die Art der Prüfung."];
            }

            if (!string.IsNullOrWhiteSpace(examDate))
            {
                if (!DateOnly.TryParseExact(examDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    errors[nameof(ExamDate)] = ["Das Datum ist ungültig."];
                }
                else if (date < StudyDay.Of(utcNow))
                {
                    errors[nameof(ExamDate)] = ["Der Prüfungstermin darf nicht in der Vergangenheit liegen."];
                }
                else
                {
                    parsedExamDate = date;
                }
            }
        }
        else if (parsedKind == LearningGoalKind.FreeLearning)
        {
            if (!string.IsNullOrWhiteSpace(examType))
            {
                errors[nameof(ExamType)] = ["Beim freien Lernen gibt es keine Prüfungsart."];
            }

            if (!string.IsNullOrWhiteSpace(examDate))
            {
                errors[nameof(ExamDate)] = ["Beim freien Lernen gibt es keinen Prüfungstermin."];
            }
        }

        LearningLevel? parsedLevel = null;
        if (!string.IsNullOrWhiteSpace(level))
        {
            parsedLevel = ParseEnum<LearningLevel>(level);
            if (parsedLevel is null)
            {
                errors[nameof(Level)] = ["Wähle eine Stufe aus der Liste."];
            }
        }

        var language = string.IsNullOrWhiteSpace(outputLanguage) ? DefaultOutputLanguage : outputLanguage.Trim();
        if (!OutputLanguages.Contains(language))
        {
            errors[nameof(OutputLanguage)] = ["Wähle Deutsch oder Englisch als Ausgabesprache."];
        }

        var trimmedGoal = string.IsNullOrWhiteSpace(goal) ? null : goal.Trim();
        if (trimmedGoal?.Length > GoalMaxLength)
        {
            errors[nameof(Goal)] = [$"Das Ziel darf höchstens {GoalMaxLength} Zeichen lang sein."];
        }

        return errors.Count > 0
            ? null
            : new LearningGoalFields(parsedKind!.Value, trimmedName, parsedExamType, parsedExamDate, parsedLevel, language, trimmedGoal);
    }

    // Names only: Enum.TryParse would also take "1" or "exampreparation".
    private static T? ParseEnum<T>(string? value)
        where T : struct, Enum =>
        value is not null && Enum.GetNames<T>().Contains(value) ? Enum.Parse<T>(value) : null;
}