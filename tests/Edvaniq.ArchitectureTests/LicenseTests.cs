namespace Edvaniq.ArchitectureTests;

// docs/licenses.md: every third-party component stands in the license list with the license it really has, and every
// license there is allowed or approved.
public class LicenseTests
{
    private const string Markdown = """
        **Erlaubt** sind `MIT` und `Apache-2.0`.

        ## Liste

        ### NuGet-Pakete

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | `Yarp.ReverseProxy` | MIT | Gateway |
        | `MySql.Data` | GPL-2.0-only WITH Universal-FOSS-exception-1.0 | Vorläufige Ausnahme: DB-Verbindung |

        ### Indirekte NuGet-Pakete

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | `K4os.Hash.xxHash` | MIT | über `MySql.Data` |

        ### Tools und Programme

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | `dotnet-ef` | MIT | Migrationen |
        | gitleaks | MIT | Secret-Scan |

        ### GitHub Actions

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | `actions/checkout` | MIT | CI |

        ### Container-Images

        | Komponente | Lizenz | Einsatz |
        |---|---|---|
        | `mysql` | GPL-2.0-only | Server-Software: Datenbank |
        | Grype (`anchore/grype`) | Apache-2.0 | Scan |

        ## Vorläufige Ausnahmen

        | `Not.A.Row` | GPL-3.0-only | danach |
        """;

    [Fact]
    public void Components_MatchTheLicenseList() =>
        Rules.AssertNone(LicenseRule.Violations(Repository.Root), "The license list", "docs/licenses.md");

    [Fact]
    public void LicenseListOfTheRepository_IsRead()
    {
        var list = LicenseList.Read(File.ReadAllText(Path.Combine(Repository.Root, "docs", "licenses.md")));

        Assert.Contains("MIT", list.Allowed);
        Assert.DoesNotContain("AND", list.Allowed);
        Assert.Contains(list.Rows, row => row is { Table: LicenseList.Packages, Component: "Yarp.ReverseProxy", License: "MIT" });
        Assert.Contains(list.Rows, row => row is { Table: LicenseList.Tools, Component: "dotnet-ef" });
        Assert.Contains(list.Rows, row => row is { Table: LicenseList.Images, Component: "mysql", IsApproved: true });
    }

    [Fact]
    public void List_IsReadFromItsTables()
    {
        var list = LicenseList.Read(Markdown);

        Assert.Equal(["Apache-2.0", "MIT"], list.Allowed.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Yarp.ReverseProxy", "MySql.Data", "K4os.Hash.xxHash", "dotnet-ef", "gitleaks", "actions/checkout", "mysql", "anchore/grype"],
            list.Rows.Select(row => row.Component));
        Assert.Equal(LicenseList.IndirectPackages, list.Rows[2].Table);
    }

    [Theory]
    [InlineData("MIT", true)]
    [InlineData("MIT AND Apache-2.0", true)]
    [InlineData("(MIT OR GPL-3.0-only)", true)]
    [InlineData("MIT AND (Apache-2.0 OR GPL-3.0-only)", true)]
    [InlineData("MIT AND GPL-3.0-only", false)]
    [InlineData("GPL-2.0-only WITH Universal-FOSS-exception-1.0", false)]
    [InlineData("Lizenzbedingungen von Microsoft", false)]
    public void Expression_IsAllowedPartByPart(string expression, bool allowed) =>
        Assert.Equal(allowed, LicenseList.Read(Markdown).IsAllowed(expression));

    [Fact]
    public void ListedComponents_AreNotReported()
    {
        var used = Used(
            Package("Yarp.ReverseProxy", "MIT"),
            Package("MySql.Data", "GPL-2.0-only WITH Universal-FOSS-exception-1.0"),
            Package("K4os.Hash.xxHash", null),
            Package("Microsoft.Extensions.Primitives", "MIT"),
            Package("Polly.Core", "MIT AND Apache-2.0"));

        Assert.Empty(LicenseRule.Violations(LicenseList.Read(Markdown), used));
    }

    [Fact]
    public void UnlistedDirectPackage_IsReported()
    {
        var used = Used(Package("Yarp.ReverseProxy", "MIT"), Package("MySql.Data", null), Package("Newtonsoft.Json", "MIT")) with
        {
            Direct = new HashSet<string>(["Yarp.ReverseProxy", "MySql.Data", "Newtonsoft.Json"]),
        };

        var violations = LicenseRule.Violations(LicenseList.Read(Markdown), used);

        Assert.Equal(["Newtonsoft.Json is a direct package but not listed under \"NuGet-Pakete\""], violations);
    }

    [Fact]
    public void ListedPackageThatIsNoLongerDirect_IsReported()
    {
        var used = Used(Package("Yarp.ReverseProxy", "MIT")) with { Direct = new HashSet<string>(["Yarp.ReverseProxy"]) };

        var violations = LicenseRule.Violations(LicenseList.Read(Markdown), used);

        Assert.Equal(["MySql.Data is listed under \"NuGet-Pakete\" but no direct package"], violations);
    }

    [Fact]
    public void LicenseThatDiffersFromTheNuspec_IsReported()
    {
        var used = Used(Package("Yarp.ReverseProxy", "BUSL-1.1"));

        var violations = LicenseRule.Violations(LicenseList.Read(Markdown), used);

        Assert.Equal(["Yarp.ReverseProxy 1.0.0 (in Edvaniq.Gateway) has the license BUSL-1.1 in its nuspec, but the list says MIT"], violations);
    }

    [Fact]
    public void IndirectPackageWithoutSpdxLicense_IsReported()
    {
        var violations = LicenseRule.Violations(LicenseList.Read(Markdown), Used(Package("Fractions", null)));

        Assert.Equal(
            ["Fractions 1.0.0 (in Edvaniq.Gateway) names its license only as a file or link and is not listed under \"Indirekte NuGet-Pakete\""],
            violations);
    }

    [Fact]
    public void IndirectPackageWithAForbiddenLicense_IsReported()
    {
        var violations = LicenseRule.Violations(LicenseList.Read(Markdown), Used(Package("iText", "AGPL-3.0-only")));

        Assert.Equal(["iText 1.0.0 (in Edvaniq.Gateway) has the license AGPL-3.0-only, which is not allowed, and is not listed"], violations);
    }

    [Fact]
    public void PackageWithoutNuspec_IsReported()
    {
        var used = Used(Package("Polly.Core", null) with { HasNuspec = false });

        var violations = LicenseRule.Violations(LicenseList.Read(Markdown), used);

        Assert.Equal(["Polly.Core 1.0.0 (in Edvaniq.Gateway) has no nuspec in the package folders. Run dotnet restore first."], violations);
    }

    [Fact]
    public void ForbiddenLicenseWithoutApproval_IsReported()
    {
        var list = LicenseList.Read(Markdown.Replace("Vorläufige Ausnahme: DB-Verbindung", "DB-Verbindung"));

        var violations = LicenseRule.Violations(list, Used());

        Assert.Equal(
            ["MySql.Data is listed with the license GPL-2.0-only WITH Universal-FOSS-exception-1.0, which is not allowed and not approved"],
            violations);
    }

    [Fact]
    public void UnlistedToolActionAndImage_AreReported()
    {
        var used = Used() with
        {
            Tools = new HashSet<string>(["dotnet-ef", "dotnet-format"]),
            Actions = new HashSet<string>(["actions/checkout", "actions/upload-artifact"]),
            Images = new HashSet<string>(["mysql", "anchore/grype", "rabbitmq"]),
        };

        var violations = LicenseRule.Violations(LicenseList.Read(Markdown), used);

        Assert.Equal(
            [
                "dotnet-format is used but not listed under \"Tools und Programme\"",
                "actions/upload-artifact is used but not listed under \"GitHub Actions\"",
                "rabbitmq is used but not listed under \"Container-Images\"",
            ],
            violations);
    }

    [Fact]
    public void Read_FindsTheComponentsOfTheRepository()
    {
        using var repository = new FakeRepository();
        repository
            .File("Edvaniq.slnx", """
                <Solution>
                  <Project Path="src/Gateway/Edvaniq.Gateway/Edvaniq.Gateway.csproj" />
                  <Project Path="src/Aspire/Edvaniq.AppHost/Edvaniq.AppHost.csproj" />
                </Solution>
                """)
            .File("Directory.Packages.props", """
                <Project><ItemGroup><PackageVersion Include="Yarp.ReverseProxy" Version="2.3.0" /></ItemGroup></Project>
                """)
            .File("src/Gateway/Edvaniq.Gateway/Edvaniq.Gateway.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />")
            .File("src/Aspire/Edvaniq.AppHost/Edvaniq.AppHost.csproj", "<Project Sdk=\"Aspire.AppHost.Sdk/13.6.0\" />")
            .File("src/Gateway/Edvaniq.Gateway/obj/project.assets.json", $$"""
                {
                  "libraries": {
                    "Yarp.ReverseProxy/2.3.0": { "type": "package", "path": "yarp.reverseproxy/2.3.0", "files": [ "yarp.reverseproxy.nuspec" ] },
                    "K4os.Hash.xxHash/1.0.8": { "type": "package", "path": "k4os.hash.xxhash/1.0.8", "files": [ "k4os.hash.xxhash.nuspec" ] },
                    "Gone/1.0.0": { "type": "package", "path": "gone/1.0.0", "files": [ "gone.nuspec" ] },
                    "Edvaniq.ServiceDefaults/1.0.0": { "type": "project" }
                  },
                  "packageFolders": { {{System.Text.Json.JsonSerializer.Serialize(repository.Path("packages"))}}: {} }
                }
                """)
            .File("packages/yarp.reverseproxy/2.3.0/yarp.reverseproxy.nuspec", """
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata><id>Yarp.ReverseProxy</id><license type="expression">MIT</license></metadata>
                </package>
                """)
            .File("packages/k4os.hash.xxhash/1.0.8/k4os.hash.xxhash.nuspec", """
                <package><metadata><id>K4os.Hash.xxHash</id><licenseUrl>https://example.org/LICENSE</licenseUrl></metadata></package>
                """)
            .File("dotnet-tools.json", """{ "version": 1, "tools": { "dotnet-ef": { "version": "10.0.12" } } }""")
            .File(".github/workflows/ci.yml", """
                jobs:
                  scan:
                    env:
                      GRYPE_IMAGE: anchore/grype:v0.119.0@sha256:0123
                    steps:
                      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
                """)
            .File("deploy/compose.yml", """
                services:
                  web:
                    image: ghcr.io/nullradix-dev/edvaniq/web:sha-${APP_COMMIT:?}
                  db:
                    image: mysql:9.7
                """)
            .File("tests/Edvaniq.Testing/MySqlTestServer.cs", """private const string Image = "mysql:9.7";""");

        var used = LicenseRule.Read(repository.Root);

        Assert.Equal(["Aspire.AppHost.Sdk", "Yarp.ReverseProxy"], used.Direct.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                new LicenseRule.Package("Yarp.ReverseProxy", "2.3.0", "Edvaniq.Gateway", "MIT", true),
                new LicenseRule.Package("K4os.Hash.xxHash", "1.0.8", "Edvaniq.Gateway", null, true),
                new LicenseRule.Package("Gone", "1.0.0", "Edvaniq.Gateway", null, false),
            ],
            used.Packages);
        Assert.Equal(["dotnet-ef"], used.Tools);
        Assert.Equal(["actions/checkout"], used.Actions);
        Assert.Equal(["anchore/grype", "mysql"], used.Images.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Edvaniq.AppHost is not restored, " + Path.Combine("src", "Aspire", "Edvaniq.AppHost", "obj", "project.assets.json") + " is missing. Run dotnet restore first."],
            LicenseRule.NotRestored(repository.Root));
    }

    private static LicenseRule.Package Package(string id, string? license) => new(id, "1.0.0", "Edvaniq.Gateway", license, true);

    // The components of a repository whose direct packages are Yarp.ReverseProxy and MySql.Data, with the packages
    // given and every tool, action and image the list knows.
    private static LicenseRule.Components Used(params LicenseRule.Package[] packages) => new(
        new HashSet<string>(["Yarp.ReverseProxy", "MySql.Data"]),
        packages,
        new HashSet<string>(["dotnet-ef"]),
        new HashSet<string>(["actions/checkout"]),
        new HashSet<string>(["mysql", "anchore/grype"]));

    // A throwaway repository with hand-written files, so a test can read components from it.
    private sealed class FakeRepository : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("edvaniq-licenses-").FullName;

        public string Path(string relative) => System.IO.Path.Combine(Root, relative);

        public FakeRepository File(string relative, string content)
        {
            var file = Path(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, content);
            return this;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}