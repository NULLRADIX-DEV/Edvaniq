namespace Edvaniq.ArchitectureTests;

// A throwaway repository with hand-written project files under src/Services, so a test can show that a rule reports a
// violation planted on purpose.
internal sealed class FakeServices : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("edvaniq-architecture-").FullName;

    public FakeServices Project(string service, string name, params string[] references)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Root, "src", "Services", service, name)).FullName;
        var items = string.Concat(references.Select(reference =>
            $"""

                <ProjectReference Include="..\..\Other\{reference}\{reference}.csproj" />
            """));
        File.WriteAllText(Path.Combine(folder, name + ".csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>{items}
              </ItemGroup>
            </Project>
            """);
        return this;
    }

    // obj/project.assets.json of a project, as a restore writes it: every package and project it depends on, also
    // through others, under targets, and its own framework references under project.
    public FakeServices Assets(string service, string name, string libraries = "", string frameworks = "\"Microsoft.NETCore.App\": {}")
    {
        var folder = Directory.CreateDirectory(Path.Combine(Root, "src", "Services", service, name, "obj")).FullName;
        File.WriteAllText(Path.Combine(folder, "project.assets.json"), $$"""
            {
              "targets": { "net10.0": { {{libraries}} } },
              "project": { "frameworks": { "net10.0": { "frameworkReferences": { {{frameworks}} } } } }
            }
            """);
        return this;
    }

    public IReadOnlyList<ServiceProject> Read() => ServiceProject.ReadAll(Root);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}