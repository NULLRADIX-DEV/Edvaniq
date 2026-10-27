namespace Edvaniq.Services.Content.Domain;

// Where a piece lies in a text.
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

// Cuts a text into the spans of its sections (docs/spec/glossary.md#abschnitt). Paragraphs end at blank lines and before
// headings ("# ", "## "). Short paragraphs are put together up to TargetLength, a heading always starts a new section
// and keeps the paragraph below it, and a paragraph over MaxLength is cut at a sentence end. Every span starts and ends
// on a character that is not white space, and only white space lies between two of them, so no word gets lost.
public static class SectionSplitter
{
    public const int TargetLength = 1_500;

    public const int MaxLength = 3_000;

    public static IReadOnlyList<TextSpan> Split(string text)
    {
        var sections = new List<TextSpan>();
        var start = -1;
        var end = 0;
        var hasBody = false;
        foreach (var piece in Paragraphs(text).SelectMany(paragraph => Cut(text, paragraph)))
        {
            var heading = IsHeading(text, piece.Start);

            // Headings without a paragraph yet wait for one, so no section is a bare heading.
            if (start < 0 || (hasBody && (heading || piece.End - start > TargetLength)))
            {
                if (start >= 0)
                {
                    sections.Add(new(start, end - start));
                }

                start = piece.Start;
                hasBody = false;
            }

            end = piece.End;
            hasBody |= !heading || text.AsSpan(piece.Start, piece.Length).Contains('\n');
        }

        if (start >= 0)
        {
            sections.Add(new(start, end - start));
        }

        return sections;
    }

    // Runs of lines with content, broken at blank lines and before every heading, without the white space around them.
    // A '\r' of a Windows line end counts as white space, so "\r\n\r\n" is a blank line too.
    private static IEnumerable<TextSpan> Paragraphs(string text)
    {
        var start = -1;
        var end = 0;
        for (var lineStart = 0; lineStart < text.Length;)
        {
            var newline = text.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? text.Length : newline;
            var first = lineStart;
            while (first < lineEnd && char.IsWhiteSpace(text[first]))
            {
                first++;
            }

            var last = lineEnd;
            while (last > first && char.IsWhiteSpace(text[last - 1]))
            {
                last--;
            }

            if (start >= 0 && (first == last || IsHeading(text, first)))
            {
                yield return new(start, end - start);
                start = -1;
            }

            if (first < last)
            {
                start = start < 0 ? first : start;
                end = last;
            }

            lineStart = lineEnd + 1;
        }

        if (start >= 0)
        {
            yield return new(start, end - start);
        }
    }

    // The paragraph itself, or pieces of at most MaxLength if it is longer.
    private static IEnumerable<TextSpan> Cut(string text, TextSpan paragraph)
    {
        var start = paragraph.Start;
        while (paragraph.End - start > MaxLength)
        {
            var cut = CutPoint(text, start, start + MaxLength);
            var end = cut;
            while (char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }

            yield return new(start, end - start);

            // The paragraph ends on content, so this stops before its end.
            start = cut;
            while (char.IsWhiteSpace(text[start]))
            {
                start++;
            }
        }

        yield return new(start, paragraph.End - start);
    }

    // Right after the last sentence end before limit, else at the last white space, else at limit itself. Only in the
    // second half of the piece, so a sentence end near its start does not leave a tiny piece behind.
    private static int CutPoint(string text, int start, int limit)
    {
        var half = start + (limit - start) / 2;
        for (var index = limit - 1; index > half; index--)
        {
            if (char.IsWhiteSpace(text[index]) && text[index - 1] is '.' or '!' or '?')
            {
                return index;
            }
        }

        for (var index = limit - 1; index > half; index--)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        // Not between the two halves of an emoji or another character outside the BMP.
        return char.IsLowSurrogate(text[limit]) ? limit - 1 : limit;
    }

    // One to six '#' and a space, as in Markdown.
    private static bool IsHeading(string text, int start)
    {
        var index = start;
        while (index < text.Length && text[index] == '#')
        {
            index++;
        }

        return index > start && index - start <= 6 && index < text.Length && text[index] == ' ';
    }
}
