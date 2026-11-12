using System.Xml.Linq;

namespace Edvaniq.ArchitectureTests;

// #221: the views live in the UI that web and app share, neither in a host nor tied to one.
public class SharedViewsTests
{
    [Fact]
    public void Views_LiveOnlyInTheSharedUi() =>
        Rules.AssertNone(SharedViewsRule.Violations(Repository.Root), "The shared UI rule", "docs/decisions/android-app.md");

    [Fact]
    public void PageOutsideClientUi_IsReported()
    {
        string[] page = ["@page \"/themen\"", "<h1>Themen</h1>"];

        Assert.Equal(
            ["src/Clients/Edvaniq.Web/Components/Pages/Topics.razor is a page outside Client.UI"],
            SharedViewsRule.CheckComponent("src/Clients/Edvaniq.Web/Components/Pages/Topics.razor", page));
        Assert.Empty(SharedViewsRule.CheckComponent("src/Clients/Edvaniq.Client.UI/Pages/Topics.razor", page));
        Assert.Empty(SharedViewsRule.CheckComponent("src/Clients/Edvaniq.Web/Components/Pages/Error.razor", ["@page \"/Error\""]));
    }

    [Fact]
    public void ComponentInAHost_IsReported()
    {
        Assert.Equal(
            ["src/Clients/Edvaniq.App/Components/Today.razor is a component in a host, it belongs in Client.UI"],
            SharedViewsRule.CheckComponent("src/Clients/Edvaniq.App/Components/Today.razor", ["<p>Heute</p>"]));
        Assert.Equal(
            ["src/Clients/Edvaniq.Web.Client/Pages/Quiz.razor is a component in a host, it belongs in Client.UI"],
            SharedViewsRule.CheckComponent("src/Clients/Edvaniq.Web.Client/Pages/Quiz.razor", ["@page \"/quiz\""]));
        Assert.Empty(SharedViewsRule.CheckComponent("src/Clients/Edvaniq.Web.Client/_Imports.razor", ["@using Edvaniq.Client.UI"]));
    }

    [Fact]
    public void SharedProjectTiedToAHost_IsReported()
    {
        var project = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <ItemGroup>
                <PackageReference Include="Microsoft.AspNetCore.Components.Web" />
                <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" />
                <PackageReference Include="Microsoft.Maui.Controls" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\Edvaniq.Client.Core\Edvaniq.Client.Core.csproj" />
                <ProjectReference Include="..\Edvaniq.App\Edvaniq.App.csproj" />
                <ProjectReference Include="..\Edvaniq.Web.Client\Edvaniq.Web.Client.csproj" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(
            [
                "UI.csproj references the package Microsoft.AspNetCore.Components.WebAssembly of one host",
                "UI.csproj references the package Microsoft.Maui.Controls of one host",
                "UI.csproj references the host Edvaniq.App",
                "UI.csproj references the host Edvaniq.Web.Client",
            ],
            SharedViewsRule.CheckProject("UI.csproj", project));
    }
}