using System.Text;

namespace Edvaniq.SharedKernel;

// When two topic names count as the same (tests/ReferenceSet/README.md): umlauts and ß spelled out, lower case, every
// character but letters and digits a space, single spaces. "pH-Wert" becomes "ph wert". Knowledge merges topics by it,
// the reference set counts its hits by it, so both agree on what the same topic is.
public static class TopicName
{
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
}