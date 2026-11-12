namespace Edvaniq.Services.Planning.Domain;

// The block "Jetzt lernen" goes on with (docs/spec/acceptance.md A61, #407).
public static class NextBlock
{
    // The first open block that was started already (its session runs, or ended without finishing it), whatever its
    // kind, else the first open one, both in plan order. Null when nothing is open: then the goal is reached or the day
    // has no blocks (DailyGoal tells which). Started ids that aren't in the plan any more change nothing.
    public static DayBlock? Of(IReadOnlyList<DayBlock> blocks, IReadOnlySet<Guid> startedBlockIds)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(startedBlockIds);

        return blocks.FirstOrDefault(block => !block.Done && startedBlockIds.Contains(block.Id))
            ?? blocks.FirstOrDefault(block => !block.Done);
    }
}