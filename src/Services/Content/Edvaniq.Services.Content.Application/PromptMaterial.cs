using System.Net;
using System.Text;

namespace Edvaniq.Services.Content.Application;

// The material of a prompt as data: numbered sections in a block of their own, with every tag inside a text escaped, so
// no text can close the block and speak as the prompt.
internal static class PromptMaterial
{
    // The sections in their order, numbered from 1.
    public static string Of(IEnumerable<string> sections)
    {
        var text = new StringBuilder("<material>\n");
        var number = 0;
        foreach (var section in sections)
        {
            text.Append($"<section id=\"{++number}\">\n").Append(Escape(section)).Append("\n</section>\n");
        }

        return text.Append("</material>").ToString();
    }

    // A section as the AI read it, on one line: escaped for the prompt and the escapes read back, so a quote of it matches
    // whether the AI kept the escapes or not.
    public static string Seen(string section) => Line(Escape(section));

    // One line, without the escapes of the prompt.
    public static string Line(string text) =>
        string.Join(' ', WebUtility.HtmlDecode(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // A topic name from an earlier answer, escaped and on one line, so it can't end its block either.
    public static string Name(string name) =>
        Escape(string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}