using System.Security.Cryptography;
using System.Text;

namespace Edvaniq.Services.Planning.Domain;

// A block of a study day with its id and whether it is done.
public sealed record DayBlock(Guid Id, Block Block, bool Done);

// The blocks of one study day: the ones done that day stay as they were, the open ones come from the fresh plan (#375).
// A pure function, the same input always gives the same blocks with the same ids.
public static class DayBlocks
{
    // Done first, by the time they were done. Each done block takes the fresh block with its id out of the plan, or else
    // the first one of the same topic and kind, so nothing is planned twice. The open ones follow in the planner's order
    // as long as done and open together stay within the day's budget, so a mastery that changed after a block can't
    // make the day longer.
    public static IReadOnlyList<DayBlock> WithDone(
        string ownerId, Guid learningGoalId, DateOnly studyDay, IReadOnlyList<Block> fresh, IReadOnlyList<DoneBlock> done, int budgetMinutes)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(done);

        var open = WithIds(ownerId, learningGoalId, studyDay, fresh);
        var finished = done.Where(block => block.StudyDay == studyDay).OrderBy(block => block.DoneAt).ThenBy(block => block.Id).ToList();
        foreach (var block in finished)
        {
            var match = open.FindIndex(candidate => candidate.Id == block.Id);
            if (match < 0)
            {
                match = open.FindIndex(candidate => candidate.Block.TopicId == block.TopicId && candidate.Block.Kind == block.Kind);
            }

            if (match >= 0)
            {
                open.RemoveAt(match);
            }
        }

        var used = finished.Sum(block => block.Minutes);
        var result = finished.Select(block => new DayBlock(block.Id, block.Block, Done: true)).ToList();
        foreach (var block in open)
        {
            if (used + block.Block.Minutes <= budgetMinutes)
            {
                result.Add(block);
                used += block.Block.Minutes;
            }
        }

        return result;
    }

    // The blocks of a fresh plan with their ids, all open.
    public static List<DayBlock> WithIds(string ownerId, Guid learningGoalId, DateOnly studyDay, IReadOnlyList<Block> blocks)
    {
        var seen = new Dictionary<(Guid, BlockKind), int>();
        var result = new List<DayBlock>(blocks.Count);
        foreach (var block in blocks)
        {
            var key = (block.TopicId, block.Kind);
            var occurrence = seen.GetValueOrDefault(key);
            seen[key] = occurrence + 1;
            result.Add(new DayBlock(IdFor(ownerId, learningGoalId, studyDay, block.TopicId, block.Kind, occurrence), block, Done: false));
        }

        return result;
    }

    // A name-based UUID (RFC 9562, version 8): the n-th block of a topic and kind on a day of a goal always has the same
    // id, so a session started on it and the block it finishes are the same. Anyone who knows the inputs can work it
    // out, so it never guards anything: who sees a block is up to the owner filter alone.
    public static Guid IdFor(string ownerId, Guid learningGoalId, DateOnly studyDay, Guid topicId, BlockKind kind, int occurrence)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        var name = $"edvaniq:block:{ownerId}:{learningGoalId:N}:{studyDay:yyyy-MM-dd}:{topicId:N}:{kind}:{occurrence}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var bytes = hash.AsSpan(0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}