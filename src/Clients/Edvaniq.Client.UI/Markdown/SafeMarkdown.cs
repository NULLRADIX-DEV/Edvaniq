using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Edvaniq.Client.UI.Markdown;

// Markdown from the user or the AI, built as elements from the parsed tree. No HTML ever comes from the text: raw HTML
// is off in the parser, only the nodes below become elements, every attribute is set here, and whatever else the
// tree holds shows as the characters it was written with.
public sealed class SafeMarkdown : ComponentBase
{
    private const string NewTab = " (öffnet in neuem Tab)";

    // Deeper than this, nested quotes and lists show as text. Keeps the render tree flat for any input.
    private const int MaxDepth = 12;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePreciseSourceLocation()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    private static readonly string[] Schemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    [Parameter]
    public string? Text { get; set; }

    // Only emphasis and code, for places that take no blocks and no links, like a legend or a label.
    [Parameter]
    public bool Inline { get; set; }

    // Added to each heading level, so the text's headings sit below the page's own.
    [Parameter]
    public int HeadingOffset { get; set; }

    public static MarkdownDocument Parse(string text) => Markdig.Markdown.Parse(text, Pipeline);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var text = Text ?? "";
        MarkdownDocument document;
        try
        {
            document = Parse(text);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InsufficientExecutionStackException)
        {
            // Markdig refuses text nested too deep. It shows as it was written then.
            if (Inline)
            {
                builder.AddContent(0, text);
                return;
            }

            builder.OpenElement(1, "div");
            builder.AddAttribute(2, "class", "markdown");
            builder.OpenElement(3, "p");
            builder.AddContent(4, text);
            builder.CloseElement();
            builder.CloseElement();
            return;
        }

        builder.AddContent(5, Render(document, text, Inline, HeadingOffset));
    }

    // The allowlist: every node type that becomes an element is named here, all others show as their source text. The
    // document needs precise source locations for that, as Parse gives them.
    public static RenderFragment Render(MarkdownDocument document, string source, bool inline = false, int headingOffset = 0) =>
        new Writer(source, inline, headingOffset).Document(document);

    private sealed class Writer(string source, bool inline, int headingOffset)
    {
        public RenderFragment Document(MarkdownDocument document) => builder =>
        {
            if (inline)
            {
                builder.AddContent(0, InlineBlocks(document, 0));
                return;
            }

            builder.OpenElement(1, "div");
            builder.AddAttribute(2, "class", "markdown");
            builder.AddContent(3, Blocks(document, tight: false, 0));
            builder.CloseElement();
        };

        private RenderFragment Blocks(ContainerBlock container, bool tight, int depth) => builder =>
        {
            foreach (var block in container)
            {
                builder.AddContent(0, Block(block, tight, depth));
            }
        };

        private RenderFragment Block(Block block, bool tight, int depth) => builder =>
        {
            if (depth > MaxDepth)
            {
                Element(builder, "p", Source(block));
                return;
            }

            switch (block)
            {
                case ParagraphBlock paragraph when tight:
                    builder.AddContent(0, Inlines(paragraph.Inline, depth));
                    break;
                case ParagraphBlock paragraph:
                    Element(builder, "p", Inlines(paragraph.Inline, depth));
                    break;
                case HeadingBlock heading:
                    Element(builder, $"h{Math.Clamp(heading.Level + headingOffset, 1, 6)}", Inlines(heading.Inline, depth));
                    break;
                case ListBlock list:
                    List(builder, list, depth);
                    break;
                case QuoteBlock quote:
                    Element(builder, "blockquote", Blocks(quote, tight: false, depth + 1));
                    break;
                case CodeBlock code:
                    builder.OpenElement(1, "pre");
                    Element(builder, "code", Lines(code));
                    builder.CloseElement();
                    break;
                case ThematicBreakBlock:
                    builder.OpenElement(2, "hr");
                    builder.CloseElement();
                    break;
                case Table table:
                    TableElement(builder, table, depth);
                    break;
                case LinkReferenceDefinitionGroup:
                    break;
                default:
                    Element(builder, "p", Source(block));
                    break;
            }
        };

        private void List(RenderTreeBuilder builder, ListBlock list, int depth)
        {
            builder.OpenElement(0, list.IsOrdered ? "ol" : "ul");
            if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start) && start != 1)
            {
                builder.AddAttribute(1, "start", start);
            }

            foreach (var item in list)
            {
                builder.OpenElement(2, "li");
                builder.AddContent(3, item is ListItemBlock listItem ? Blocks(listItem, !list.IsLoose, depth + 1) : Source(item));
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        private void TableElement(RenderTreeBuilder builder, Table table, int depth)
        {
            // Wide tables scroll on their own instead of the page; the region lets a keyboard scroll it too.
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "markdown-table");
            builder.AddAttribute(2, "role", "region");
            builder.AddAttribute(3, "aria-label", "Tabelle");
            builder.AddAttribute(4, "tabindex", "0");
            builder.OpenElement(5, "table");
            var rows = table.OfType<TableRow>().ToList();
            Section(builder, "thead", rows.Where(row => row.IsHeader), table, depth);
            Section(builder, "tbody", rows.Where(row => !row.IsHeader), table, depth);
            builder.CloseElement();
            builder.CloseElement();
        }

        private void Section(RenderTreeBuilder builder, string name, IEnumerable<TableRow> rows, Table table, int depth)
        {
            var list = rows.ToList();
            if (list.Count == 0)
            {
                return;
            }

            builder.OpenElement(0, name);
            foreach (var row in list)
            {
                builder.OpenElement(1, "tr");
                var column = 0;
                foreach (var cell in row.OfType<TableCell>())
                {
                    builder.OpenElement(2, row.IsHeader ? "th" : "td");
                    if (row.IsHeader)
                    {
                        builder.AddAttribute(3, "scope", "col");
                    }

                    if (Alignment(table, column++) is { } alignment)
                    {
                        builder.AddAttribute(4, "class", alignment);
                    }

                    builder.AddContent(5, Blocks(cell, tight: true, depth + 1));
                    builder.CloseElement();
                }

                builder.CloseElement();
            }

            builder.CloseElement();
        }

        private static string? Alignment(Table table, int column) =>
            column < table.ColumnDefinitions.Count
                ? table.ColumnDefinitions[column].Alignment switch
                {
                    TableColumnAlign.Center => "align-center",
                    TableColumnAlign.Right => "align-right",
                    _ => null,
                }
                : null;

        private RenderFragment Inlines(ContainerInline? container, int depth) => builder =>
        {
            if (container is null)
            {
                return;
            }

            foreach (var item in container)
            {
                builder.AddContent(0, InlineNode(item, depth));
            }
        };

        private RenderFragment InlineNode(Inline node, int depth) => builder =>
        {
            if (depth > MaxDepth)
            {
                builder.AddContent(0, Source(node));
                return;
            }

            switch (node)
            {
                case LiteralInline literal:
                    builder.AddContent(1, literal.Content.ToString());
                    break;
                case HtmlEntityInline entity:
                    builder.AddContent(2, entity.Transcoded.ToString());
                    break;
                case LineBreakInline { IsHard: true } when !inline:
                    builder.OpenElement(3, "br");
                    builder.CloseElement();
                    break;
                case LineBreakInline:
                    builder.AddContent(4, "\n");
                    break;
                case CodeInline code:
                    Element(builder, "code", code.Content);
                    break;
                case EmphasisInline { DelimiterChar: '*' or '_', DelimiterCount: 1 } emphasis:
                    Element(builder, "em", Inlines(emphasis, depth + 1));
                    break;
                case EmphasisInline { DelimiterChar: '*' or '_', DelimiterCount: 2 } emphasis:
                    Element(builder, "strong", Inlines(emphasis, depth + 1));
                    break;
                case EmphasisInline { DelimiterChar: '~', DelimiterCount: 2 } emphasis:
                    Element(builder, "del", Inlines(emphasis, depth + 1));
                    break;
                case LinkInline { IsImage: true } image:
                    // Never loaded: an image would call a foreign host. Its description stands in for it.
                    builder.AddContent(5, Inlines(image, depth + 1));
                    break;
                case LinkInline link:
                    Link(builder, link.Url, Inlines(link, depth + 1));
                    break;
                case AutolinkInline autolink:
                    Link(builder, autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url, Text(autolink.Url));
                    break;
                default:
                    builder.AddContent(6, Source(node));
                    break;
            }
        };

        private void Link(RenderTreeBuilder builder, string? url, RenderFragment content)
        {
            if (inline || Target(url) is not { } target)
            {
                builder.AddContent(0, content);
                return;
            }

            builder.OpenElement(1, "a");
            builder.AddAttribute(2, "href", target.AbsoluteUri);
            if (target.Scheme != Uri.UriSchemeMailto)
            {
                builder.AddAttribute(3, "target", "_blank");
                builder.AddAttribute(4, "rel", "noopener noreferrer nofollow");
            }

            builder.AddContent(5, content);
            if (target.Scheme != Uri.UriSchemeMailto)
            {
                builder.OpenElement(6, "span");
                builder.AddAttribute(7, "class", "visually-hidden");
                builder.AddContent(8, NewTab);
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        // Only absolute web and mail addresses; anything else, javascript: and data: included, stays text.
        private static Uri? Target(string? url) =>
            Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && Schemes.Contains(uri.Scheme) ? uri : null;

        // Blocks reduced to their inline text, one after the other.
        private RenderFragment InlineBlocks(ContainerBlock container, int depth) => builder =>
        {
            var first = true;
            foreach (var block in container)
            {
                if (block is LinkReferenceDefinitionGroup)
                {
                    continue;
                }

                if (!first)
                {
                    builder.AddContent(0, " ");
                }

                first = false;
                builder.AddContent(1, block switch
                {
                    _ when depth > MaxDepth => Source(block),
                    LeafBlock { Inline: { } inlines } => Inlines(inlines, depth),
                    CodeBlock code => Lines(code),
                    ContainerBlock children and not Table => InlineBlocks(children, depth + 1),
                    _ => Source(block),
                });
            }
        };

        private static RenderFragment Lines(LeafBlock block) => Text(block.Lines.ToString());

        private RenderFragment Source(MarkdownObject node) => Text(
            node.Span.Start >= 0 && node.Span.End < source.Length && node.Span.Length > 0
                ? source.Substring(node.Span.Start, node.Span.Length)
                : "");

        private static RenderFragment Text(string text) => builder => builder.AddContent(0, text);

        private static void Element(RenderTreeBuilder builder, string name, RenderFragment content)
        {
            builder.OpenElement(10, name);
            builder.AddContent(11, content);
            builder.CloseElement();
        }

        private static void Element(RenderTreeBuilder builder, string name, string text)
        {
            builder.OpenElement(12, name);
            builder.AddContent(13, text);
            builder.CloseElement();
        }
    }
}