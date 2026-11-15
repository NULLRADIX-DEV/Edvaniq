namespace Edvaniq.Services.LearningEngine.Domain;

// Where a topic stood at a past moment (#403): mastery and state as TopicStates.Assess would have given them then, from
// the records and the introduction there were by that moment. A session's result takes it at its start and its end, so
// the same session always shows the same change, however much later it's asked. The same Assess as everywhere, so a
// change of who counts reaches this too.
public static class MasteryChange
{
    // Null when the moment lies before the carry's last record (#365): those records are gone, so the topic can't be
    // told apart at that moment any more.
    public static TopicStanding? At(
        IEnumerable<LearningRecord> records, TopicIntroduction? introduction, DateTime at, MasteryCarry? carry = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (carry?.At is { } carried && carried > at)
        {
            return null;
        }

        var then = introduction is not null && introduction.IntroducedAt <= at ? introduction : null;
        return TopicStates.Assess(records.Where(record => record.AnsweredAt <= at), then, at, carry: carry);
    }
}