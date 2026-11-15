using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.ArchitectureTests;

// #319: one upload limit along the whole way (docs/architecture.md, "Upload-Grenze"). The browser checks the file, the
// web host, the gateway and Content the body with its multipart frame. They live in three processes and two
// configuration files, so this test keeps them equal: a change in one place turns it red until the others follow.
public partial class UploadLimitTests
{
    [Fact]
    public void WebHostGatewayAndContent_TakeTheSameBody()
    {
        var web = Settings("src/Clients/Edvaniq.Web/appsettings.json")["ReverseProxy"]!["Routes"]!["files"]!["Metadata"]!["MaxRequestBodySize"]!;
        var gateway = Settings("src/Gateway/Edvaniq.Gateway/appsettings.json")["Kestrel"]!["Limits"]!["MaxRequestBodySize"]!;

        Assert.Equal(FileCheck.MaxBodyLength, long.Parse(web.GetValue<string>()));
        Assert.Equal(FileCheck.MaxBodyLength, gateway.GetValue<long>());
    }

    [Fact]
    public void BrowserAndContent_TakeTheSameFile()
    {
        Assert.Equal(FileCheck.MaxLength, MaterialView.UploadMaxLength);
    }

    [Fact]
    public void EveryoneRefusesWithTheSameWords()
    {
        var web = TooLarge().Match(File.ReadAllText(Path.Combine(Repository.Root, "src", "Clients", "Edvaniq.Web", "UploadLimit.cs"))).Groups[1].Value;

        Assert.Equal(FileCheck.TooLarge, FeedbackTexts.Upload.TooLarge);
        Assert.Equal(FileCheck.TooLarge, web);
    }

    private static JsonNode Settings(string path) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, path)), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;

    [GeneratedRegex(@"\bTooLarge\s*=\s*""([^""]+)""")]
    private static partial Regex TooLarge();
}