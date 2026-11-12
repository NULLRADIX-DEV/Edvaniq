using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.LearningEngine.Domain;

// What the expired learning records of one topic leave behind (Q27, #365): the fold of Mastery.Of right after the last
// of them that counted, and the newest one's answer id. Mastery, state, decay and the last answer go on from here
// exactly as if the records were still there. Nothing else of the answers is kept. RuleVersion is the rule the records
// were folded under: they are gone, so after a change of the rule the carry stays as it is and the new rule only applies
// from the carry on. Version is the concurrency token, so two runs can't fold the same records twice.
public sealed class MasteryCarry : IOwnedByUser
{
    private MasteryCarry(string ownerId, Guid topicId)
    {
        OwnerId = ownerId;
        TopicId = topicId;
    }

    public string OwnerId { get; private set; }

    public Guid TopicId { get; private set; }

    // Null while no expired record counted (all of them came before the introduction).
    public double? Value { get; private set; }

    // UTC, the time of the last counted record that went.
    public DateTime? At { get; private set; }

    public bool? LastCorrect { get; private set; }

    public bool? PreviousCorrect { get; private set; }

    public Guid LastAnswerId { get; private set; }

    public int RuleVersion { get; private set; }

    public int Version { get; private set; }

    // The fold to go on from, or null. An introduction after the carry means none of its records count, as they
    // wouldn't if they were still there.
    public MasteryFold? FoldFor(TopicIntroduction? introduction)
    {
        if (Value is not { } value || At is not { } at || LastCorrect is not { } lastCorrect)
        {
            return null;
        }

        return introduction is not null && introduction.IntroducedAt > at ? null : new MasteryFold(value, at, lastCorrect, PreviousCorrect);
    }

    // Folds the expired records of the topic into the carry, starting a new one for the first. Records are those to
    // delete, all older than any record that stays. Only those the introduction counts move the fold, the newest of all
    // gives the last answer.
    public static MasteryCarry Fold(
        MasteryCarry? carry, string ownerId, Guid topicId, IReadOnlyList<LearningRecord> records, TopicIntroduction? introduction)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            throw new ArgumentException("Nothing to fold.", nameof(records));
        }

        var result = carry ?? new MasteryCarry(ownerId, topicId);
        var counted = introduction is null ? records : [.. records.Where(introduction.Counts)];
        if (Mastery.Fold(counted, Mastery.Start, carry?.FoldFor(introduction)) is { } fold && counted.Count > 0)
        {
            (result.Value, result.At, result.LastCorrect, result.PreviousCorrect) =
                (fold.Value, DateTime.SpecifyKind(fold.At, DateTimeKind.Utc), fold.LastCorrect, fold.PreviousCorrect);
        }

        result.LastAnswerId = LearningRecord.Latest(records)!.Value;
        result.RuleVersion = Mastery.RuleVersion;
        result.Version++;
        return result;
    }

    // For tests and a carry read back as stored: one folded under an older rule.
    public static MasteryCarry Restore(
        string ownerId, Guid topicId, MasteryFold? fold, Guid lastAnswerId, int ruleVersion) =>
        new(ownerId, topicId)
        {
            Value = fold?.Value,
            At = fold is null ? null : DateTime.SpecifyKind(fold.At, DateTimeKind.Utc),
            LastCorrect = fold?.LastCorrect,
            PreviousCorrect = fold?.PreviousCorrect,
            LastAnswerId = lastAnswerId,
            RuleVersion = ruleVersion,
            Version = 1,
        };
}