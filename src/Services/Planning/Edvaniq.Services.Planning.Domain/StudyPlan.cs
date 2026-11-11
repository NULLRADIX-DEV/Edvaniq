namespace Edvaniq.Services.Planning.Domain;

// The study days with their blocks, ascending, only days that have blocks (docs/spec/glossary.md#lernplan). Shortened:
// the topics that got no learn time because the time was too short (#370), by id, empty when nothing was shortened.
public sealed record StudyPlan(IReadOnlyList<DayPlan> Days)
{
    public static StudyPlan Empty { get; } = new([]);

    public IReadOnlyList<Guid> Shortened { get; init; } = [];
}