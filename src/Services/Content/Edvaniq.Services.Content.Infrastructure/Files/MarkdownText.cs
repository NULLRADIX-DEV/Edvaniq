using System.Text;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// The text of a Markdown file without its markup, in the form the splitter reads: every heading, also one underlined
// with "===", as "# " to "###### ", blocks apart by a blank line. Emphasis marks, link targets, pictures and HTML go,
// a link keeps its text, code keeps its lines without the fences. Nothing is rendered or fetched.
public static class MarkdownText
{
    public static string Of(string markdown)
    {
        var text = new StringBuilder();
        Blocks(Markdown.Parse(markdown), text);
        return text.ToString();
    }

    private static void Blocks(ContainerBlock container, StringBuilder text)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock heading when Inlines(heading.Inline) is { Length: > 0 } title:
                    Add(text, new string('#', Math.Clamp(heading.Level, 1, 6)) + " " + title.ReplaceLineEndings(" "));
                    break;
                case HeadingBlock:
                    break;
                case ParagraphBlock paragraph:
                    Add(text, Inlines(paragraph.Inline));
                    break;
                case CodeBlock code:
                    Add(text, code.Lines.ToString());
                    break;
                case ContainerBlock inner:
                    Blocks(inner, text);
                    break;
            }
        }
    }

    private static string Inlines(ContainerInline? container)
    {
        var text = new StringBuilder();
        Inlines(container, text);
        return text.ToString().Trim();
    }

    private static void Inlines(ContainerInline? container, StringBuilder text)
    {
        for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content);
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case HtmlEntityInline entity:
                    text.Append(entity.Transcoded);
                    break;
                case LineBreakInline:
                    text.Append('\n');
                    break;
                case AutolinkInline autolink:
                    text.Append(autolink.Url);
                    break;
                case LinkInline { IsImage: true }:
                    break;
                case ContainerInline inner:
                    Inlines(inner, text);
                    break;
            }
        }
    }

    private static void Add(StringBuilder text, string block)
    {
        block = block.Trim();
        if (block.Length == 0)
        {
            return;
        }

        if (text.Length > 0)
        {
            text.Append("\n\n");
        }

        text.Append(block);
    }
}