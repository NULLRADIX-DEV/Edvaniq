using System.Text;

namespace Edvaniq.Client.Core;

// Topics the user enters by hand, in Knowledge (docs/spec/glossary.md#thema).
public interface ITopicEntries
{
    // A topic of the upper level in the learning goal. A topic of the same name that is already there counts as added
    // (AlreadyThere). Rejected says why in Messages.
    Task<CallOutcome<TopicAdded>> AddAsync(Guid learningGoalId, string name, CancellationToken cancellationToken = default);
}

public sealed record TopicAdded(bool AlreadyThere);

// The limit of Topic and the rule of TopicName in Knowledge. The client can't reference either, so TopicValuesTests in
// the architecture tests holds both sides together.
public static class TopicValues
{
    public const int NameMaxLength = 80;

    // When two names mean the same topic: umlauts and ß spelled out, lower case, every character but letters and digits
    // a space, single spaces.
    public static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä':
                    builder.Append("ae");
                    break;
                case 'ö':
                    builder.Append("oe");
                    break;
                case 'ü':
                    builder.Append("ue");
                    break;
                case 'ß':
                    builder.Append("ss");
                    break;
                default:
                    builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
                    break;
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // The names to add: trimmed, empty ones left out, and of those that mean the same only the first.
    public static IReadOnlyList<string> Distinct(IEnumerable<string?> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var name in names)
        {
            var trimmed = name?.Trim() ?? "";
            if (trimmed.Length == 0)
            {
                continue;
            }

            // A name of signs only normalizes to nothing, Knowledge compares it all the same.
            if (seen.Add(Normalize(trimmed)))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}