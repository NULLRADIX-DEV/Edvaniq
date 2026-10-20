using System.Text.RegularExpressions;

namespace Edvaniq.Client.UI.OpenSource;

// The third-party components of Edvaniq with their licenses, read from docs/licenses.md, which this project embeds: one
// group per "### " heading of the tables under "## Liste", one entry per row with the component and its license, the
// backticks taken out. The column with the use stays out, it is a note for developers.
public sealed partial record LicenseNotices(IReadOnlyList<LicenseNotices.Group> Groups)
{
    private const string Resource = "licenses.md";

    public sealed record Group(string Title, IReadOnlyList<Entry> Entries);

    public sealed record Entry(string Component, string License);

    public static LicenseNotices Embedded { get; } = Read(EmbeddedList());

    // The SPDX ids of licenses and exceptions the entries use, each once, in the order they first appear. A license that
    // is no SPDX expression, such as the license terms of a vendor, has none.
    public IReadOnlyList<string> SpdxIds =>
        Groups.SelectMany(group => group.Entries).SelectMany(entry => IdsOf(entry.License)).Distinct().ToList();

    // docs/licenses.md as it was when the client was built.
    public static string EmbeddedList()
    {
        using var stream = typeof(LicenseNotices).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The resource {Resource} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static LicenseNotices Read(string markdown)
    {
        var groups = new List<(string Title, List<Entry> Entries)>();
        var lines = markdown.Split('\n').Select(line => line.Trim());
        foreach (var line in lines.SkipWhile(line => line != "## Liste").Skip(1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                groups.Add((line[4..], []));
            }
            else if (groups.Count > 0 && line.StartsWith('|') && !line.StartsWith("| Komponente |", StringComparison.Ordinal) && !line.StartsWith("|---", StringComparison.Ordinal))
            {
                var cells = line.Split('|', StringSplitOptions.TrimEntries);
                groups[^1].Entries.Add(new Entry(cells[1].Replace("`", ""), cells[2]));
            }
        }

        return new LicenseNotices(groups.Where(group => group.Entries.Count > 0).Select(group => new Group(group.Title, group.Entries)).ToList());
    }

    // An SPDX expression alternates ids and the operators AND, OR and WITH, with parentheses around parts.
    private static IEnumerable<string> IdsOf(string license)
    {
        var tokens = license.Replace("(", " ").Replace(")", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var isExpression = tokens.Length % 2 == 1
            && tokens.Where((_, index) => index % 2 == 1).All(token => token is "AND" or "OR" or "WITH")
            && tokens.Where((_, index) => index % 2 == 0).All(token => SpdxId().IsMatch(token));
        return isExpression ? tokens.Where((_, index) => index % 2 == 0) : [];
    }

    [GeneratedRegex(@"^[A-Za-z0-9.+-]+$")]
    private static partial Regex SpdxId();
}