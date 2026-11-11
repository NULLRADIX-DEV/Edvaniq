namespace Edvaniq.Services.Planning.Domain;

// The study days with their blocks, ascending, only days that have blocks (docs/spec/glossary.md#lernplan).
public sealed record StudyPlan(IReadOnlyList<DayPlan> Days)
{
    public static StudyPlan Empty { get; } = new([]);
}