namespace Edvaniq.ArchitectureTests;

// The plain text rule in docs/architecture.md ("Clients"): text from the user or the AI is never written as markup.
public class ClientMarkupTests
{
    [Fact]
    public void Clients_NeverWriteAStringAsMarkup() =>
        Rules.AssertNone(ClientMarkupRule.Violations(Repository.Root), "The plain text rule");

    [Fact]
    public void Markup_IsReported()
    {
        string[] lines =
        [
            "<p>@topic.Name</p>",
            "<p>@((MarkupString)topic.Name)</p>",
            "builder.AddMarkupContent(0, text);",
            "element.innerHTML = text; document.write(text);",
        ];

        Assert.Equal(
            [
                "Pages/View.razor:2 uses MarkupString",
                "Pages/View.razor:3 uses AddMarkupContent",
                "Pages/View.razor:4 uses innerHTML",
                "Pages/View.razor:4 uses document.write",
            ],
            ClientMarkupRule.Check("Pages/View.razor", lines));
    }
}