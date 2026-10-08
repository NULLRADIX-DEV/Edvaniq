using System.Globalization;
using System.Text.RegularExpressions;

namespace Edvaniq.Client.UnitTests;

// WCAG 2.1 AA: text needs a contrast of at least 4.5:1 to its background, borders and controls at least 3:1.
// The colors come straight from the tokens in edvaniq.css, so a new shade cannot slip in below the line.
public sealed partial class ContrastTests
{
    public static TheoryData<string, string, double> Pairs => new()
    {
        { "--color-ink", "--color-paper", 4.5 },
        { "--color-ink", "--color-surface", 4.5 },
        { "--color-ink-muted", "--color-paper", 4.5 },
        { "--color-accent", "--color-paper", 4.5 },
        { "--color-accent", "--color-surface", 4.5 },
        { "--color-accent-hover", "--color-surface", 4.5 },
        { "--color-on-accent", "--color-accent", 4.5 },
        { "--color-on-accent", "--color-accent-hover", 4.5 },
        { "--color-on-highlight", "--color-highlight", 4.5 },
        { "--color-on-alert-surface", "--color-alert-surface", 4.5 },
        { "--color-accent", "--color-alert-surface", 3.0 },
        { "--color-alert", "--color-alert-surface", 3.0 },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Pair_HasEnoughContrast(string foreground, string background, double minimum)
    {
        var colors = Tokens();

        var ratio = Contrast(colors[foreground], colors[background]);

        Assert.True(ratio >= minimum, $"{foreground} on {background}: {ratio:F2}:1, needs {minimum}:1");
    }

    // Two known values from the WCAG examples, so the formula itself is checked too.
    [Fact]
    public void Contrast_MatchesKnownValues()
    {
        Assert.Equal(21.0, Contrast("000000", "ffffff"), precision: 2);
        Assert.Equal(4.54, Contrast("767676", "ffffff"), precision: 2);
    }

    private static double Contrast(string first, string second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    private static Dictionary<string, string> Tokens()
    {
        var css = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Clients", "Edvaniq.Client.DesignSystem", "wwwroot", "edvaniq.css"));
        return ColorToken().Matches(css).ToDictionary(match => match.Groups["name"].Value, match => match.Groups["hex"].Value);
    }

    [GeneratedRegex(@"(?<name>--[\w-]+):\s*#(?<hex>[0-9a-fA-F]{6})\s*;")]
    private static partial Regex ColorToken();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Edvaniq.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Edvaniq.slnx not found above " + AppContext.BaseDirectory);
    }
}