namespace Edvaniq.ArchitectureTests;

// The server is the only source of the user's data (docs/architecture.md, "Clients"), no client keeps a state of its own.
public class ClientStorageTests
{
    [Fact]
    public void Clients_KeepNothingInLocalStorage() =>
        Rules.AssertNone(ClientStorageRule.Violations(Repository.Root), "The rule that the server is the only source");

    [Fact]
    public void Storage_IsReported()
    {
        string[] lines =
        [
            "// The browser caches nothing, no localStorage and no sessionStorage.",
            "localStorage.setItem('draft', text); window.sessionStorage.getItem('draft');",
            "const db = indexedDB.open('edvaniq'); await caches.open('v1');",
            "Preferences.Default.Set(\"draft\", text); await SecureStorage.Default.SetAsync(\"token\", token);",
            "var userPreferences = new UserPreferences(); userPreferences.Theme = \"dark\";",
        ];

        Assert.Equal(
            [
                "Pages/View.razor:2 uses localStorage",
                "Pages/View.razor:2 uses sessionStorage",
                "Pages/View.razor:3 uses indexedDB",
                "Pages/View.razor:3 uses caches",
                "Pages/View.razor:4 uses Preferences",
                "Pages/View.razor:4 uses SecureStorage",
            ],
            ClientStorageRule.Check("Pages/View.razor", lines));
    }
}