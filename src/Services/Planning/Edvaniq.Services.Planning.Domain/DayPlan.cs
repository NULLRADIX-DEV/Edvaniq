namespace Edvaniq.Services.Planning.Domain;

// The blocks of one study day, in the order they come up (docs/spec/glossary.md#tagesplan).
public sealed record DayPlan(DateOnly StudyDay, IReadOnlyList<Block> Blocks);