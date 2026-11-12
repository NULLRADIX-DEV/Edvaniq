namespace Edvaniq.Services.Planning.Domain;

// Whether the day's goal is reached (docs/spec/glossary.md#tagesziel).
public enum DailyGoalState
{
    // No blocks today, so there is nothing to reach.
    None,

    Open,

    Reached,
}

public static class DailyGoal
{
    // Judges the blocks as today's plan shows them, after DayBlocks.WithDone and its budget cut: done blocks that filled
    // the budget leave no open ones, and that counts as reached. The blocks are those of one study day, so blocks done
    // for yesterday never count for today. An empty plan, whatever the reason, is never open.
    public static DailyGoalState Of(IReadOnlyList<DayBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Count == 0)
        {
            return DailyGoalState.None;
        }

        return blocks.All(block => block.Done) ? DailyGoalState.Reached : DailyGoalState.Open;
    }
}