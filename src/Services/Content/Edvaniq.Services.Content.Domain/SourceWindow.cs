namespace Edvaniq.Services.Content.Domain;

// The piece of a document's text that shows a section in its context (#350): the section with the one before and the
// one after it, so a phone never gets a whole long text. Start and length of the section count from the start of the
// window. Truncated says whether the document has more text before or after it.
public sealed record SourceWindow(string Text, int Start, int Length, bool TruncatedBefore, bool TruncatedAfter)
{
    // neighbours holds the section and, if there are, the one before and the one after it, in any order.
    public static SourceWindow Around(string text, Section section, IEnumerable<Section> neighbours)
    {
        var near = neighbours.Where(other => other.DocumentId == section.DocumentId && Math.Abs(other.Ordinal - section.Ordinal) <= 1).ToList();
        var from = near.Select(other => other.Start).Append(section.Start).Min();
        var to = near.Select(other => other.Start + other.Length).Append(section.Start + section.Length).Max();
        return new SourceWindow(
            text[from..to],
            section.Start - from,
            section.Length,
            !string.IsNullOrWhiteSpace(text[..from]),
            !string.IsNullOrWhiteSpace(text[to..]));
    }
}