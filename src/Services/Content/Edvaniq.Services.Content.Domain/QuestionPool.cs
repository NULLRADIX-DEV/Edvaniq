using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// Content has made the questions of a topic and sent them with ItemsGenerated. TopicId is Knowledge's id, only a
// reference. It's the key: a topic gets its pool once, a topic that comes again unchanged gets none (docs/spec/events.md).
// The questions themselves belong to Assessment from then on, Content keeps none of them, only how many it sent and a
// short hash of each (QuestionKeys), so topping the pool up sends nothing twice (#339).
public sealed class QuestionPool : IOwnedByUser
{
    // A pool with fewer questions gets topped up.
    public const int MinQuestions = 15;

    public const int MaxQuestions = 20;

    // Top-up rounds that brought an answer of the AI. One that adds nothing new would ask the same again, so it ends.
    public const int MaxTopUps = 3;

    public const int KeyLength = 16;

    // Space between the keys.
    public const int KeysMaxLength = MaxQuestions * (KeyLength + 1);

    private QuestionPool(Guid topicId, Guid documentId, string ownerId, DateTime createdAt)
    {
        TopicId = topicId;
        DocumentId = documentId;
        OwnerId = ownerId;
        CreatedAt = createdAt;
    }

    public Guid TopicId { get; private set; }

    // The document whose analysis brought the topic.
    public Guid DocumentId { get; private set; }

    public string OwnerId { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // How many questions went out. Null for a pool from before #339, which isn't topped up.
    public int? Questions { get; private set; }

    // The keys of the questions that went out, separated by spaces.
    public string? QuestionKeys { get; private set; }

    public int TopUps { get; private set; }

    // Outages of the AI in a row while topping up, the next one waits by Document.OutageDelays.
    public int TopUpOutages { get; private set; }

    // UTC. Null while a top-up may start at once.
    public DateTime? NextTopUpAt { get; private set; }

    // No more top-ups: the AI refused, stayed away or brought too little three times.
    public bool TopUpStopped { get; private set; }

    public int Missing => Math.Max(0, MaxQuestions - (Questions ?? MaxQuestions));

    public bool WantsTopUp => Questions < MinQuestions && !TopUpStopped;

    public static QuestionPool For(Document document, Guid topicId, DateTime createdAt, IReadOnlyCollection<string> keys)
    {
        var pool = new QuestionPool(topicId, document.Id, document.OwnerId, createdAt) { Questions = 0 };
        pool.Note(keys);
        return pool;
    }

    public bool Knows(string key) => QuestionKeys is { } keys && keys.Split(' ').Contains(key, StringComparer.Ordinal);

    // A round that got an answer. False if the pool stays short and gets no more rounds.
    public bool ToppedUp(IReadOnlyCollection<string> keys)
    {
        Note(keys);
        TopUps++;
        TopUpOutages = 0;
        NextTopUpAt = null;
        if (WantsTopUp && TopUps >= MaxTopUps)
        {
            TopUpStopped = true;
            return false;
        }

        return true;
    }

    // Nothing asked: the quota, the budget or the emergency stop. Tried again from then.
    public void WaitUntil(DateTime then) => NextTopUpAt = then;

    // An outage of the AI. False if there's no delay left, then the pool gets no more rounds.
    public bool Postpone(DateTime now, IReadOnlyList<TimeSpan> delays, TimeSpan? retryAfter)
    {
        if (TopUpOutages >= delays.Count)
        {
            Stop();
            return false;
        }

        var delay = delays[TopUpOutages];
        if (retryAfter > delay)
        {
            delay = retryAfter < Document.MaxOutageDelay ? retryAfter.Value : Document.MaxOutageDelay;
        }

        TopUpOutages++;
        NextTopUpAt = now + delay;
        return true;
    }

    public void Stop()
    {
        TopUpStopped = true;
        NextTopUpAt = null;
    }

    private void Note(IReadOnlyCollection<string> keys)
    {
        if (keys.Any(key => key.Length != KeyLength || key.Contains(' ', StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A key has {KeyLength} characters and no space.", nameof(keys));
        }

        var all = (QuestionKeys?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []).Concat(keys).ToList();
        if (all.Count > MaxQuestions)
        {
            throw new InvalidOperationException($"A pool holds at most {MaxQuestions} questions.");
        }

        QuestionKeys = string.Join(' ', all);
        Questions = all.Count;
    }
}