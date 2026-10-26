namespace Edvaniq.ArchitectureTests;

// docs/spec/events.md names the sender and the receivers of each event. Every receiver must be allowed to listen to
// every sender of that event by the table "Wer auf wessen Integration Events hört" in docs/architecture.md, otherwise
// the catalog would bring in an edge that rule 6 never checked for cycles.
internal static class EventCatalogRule
{
    private const string TableHeader = "| Ereignis | Sender | Auslöser | Empfänger | Empfänger im MVP-Plus |";

    // Event, consumer and sender for each row of the catalog, MVP and MVP-Plus receivers alike. Only "–" means no
    // service, anything else counts as a name, so an annotated cell shows up as a pair the table doesn't know.
    // Empty if the table is missing.
    public static IReadOnlyList<(string Event, string Consumer, string Sender)> Pairs(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.Trim()).ToList();
        var header = lines.IndexOf(TableHeader);
        return header < 0 ? [] : lines.Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .SelectMany(cells => Services(cells[4]).Concat(Services(cells[5]))
                .SelectMany(consumer => Services(cells[2]).Select(sender => (cells[1].Trim('`'), consumer, sender))))
            .ToList();
    }

    public static IEnumerable<string> Violations(
        IEnumerable<(string Event, string Consumer, string Sender)> pairs, IReadOnlyCollection<(string From, string To)> allowed) =>
        pairs.Where(pair => !allowed.Contains((pair.Consumer, pair.Sender)))
            .Select(pair => $"{pair.Consumer} receives {pair.Event} from {pair.Sender}, but the event table in docs/architecture.md doesn't let it listen to {pair.Sender}");

    private static IEnumerable<string> Services(string cell) =>
        cell == "–" ? [] : cell.Split(',', StringSplitOptions.TrimEntries);
}