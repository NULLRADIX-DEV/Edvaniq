namespace Edvaniq.Services.Content.Application;

// A long document goes to the AI in parts (#326): its sections in order, a part up to a length the model takes in well.
// One prompt with everything would pass the model's context with the longest documents, and a model loses track in the
// middle of a very long text. A document within the length stays one part, asked exactly as before.
public static class TopicBatches
{
    // About 12,000 tokens of material, far below the context of the models in question.
    public const int DefaultBatchCharacters = 40_000;

    // The sections of each part, as ranges into the sections in order. A section longer than the length is a part of its
    // own, it is never cut.
    public static IReadOnlyList<Range> Of(IReadOnlyList<int> sectionLengths, int batchCharacters = DefaultBatchCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchCharacters, 1);
        var batches = new List<Range>();
        var start = 0;
        var length = 0;
        for (var section = 0; section < sectionLengths.Count; section++)
        {
            if (section > start && length + sectionLengths[section] > batchCharacters)
            {
                batches.Add(start..section);
                start = section;
                length = 0;
            }

            length += sectionLengths[section];
        }

        if (sectionLengths.Count > start)
        {
            batches.Add(start..sectionLengths.Count);
        }

        return batches;
    }

    // How many topics each part may bring, in proportion to its length, so the end of a long document gets its topics
    // too. Each part at least one, all together never more than maxTopics (as long as there are no more parts than that).
    public static IReadOnlyList<int> Quotas(IReadOnlyList<int> batchLengths, int maxTopics)
    {
        var total = Math.Max(1, batchLengths.Sum(length => (long)length));
        var quotas = batchLengths.Select(length => Math.Max(1, (int)(maxTopics * (long)length / total))).ToArray();
        while (quotas.Sum() > maxTopics && quotas.Max() > 1)
        {
            quotas[Array.IndexOf(quotas, quotas.Max())]--;
        }

        return quotas;
    }
}