namespace Edvaniq.Services.Content.Application;

// The material of the demo learning goal (#410): texts the team wrote for it, under CC0-1.0 (docs/licenses.md,
// "Inhalte"). They ship inside this assembly, so the demo needs neither a file nor the AI to have its material (#411).
// Each text is pasted as it is, its first heading is its title.
public static class DemoMaterial
{
    private const string Prefix = "Demo.";

    public static IReadOnlyList<DemoDocument> Documents { get; } = Load();

    private static List<DemoDocument> Load()
    {
        var assembly = typeof(DemoMaterial).Assembly;
        return [.. assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
                return new DemoDocument(text.Split('\n')[0].TrimStart('#', ' '), text);
            })];
    }
}

public sealed record DemoDocument(string Title, string Text);