using System.Reflection;

using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.ArchitectureTests;

// docs/spec/events.md is the catalog of integration events. It names exactly the nine events of the MVP, and every event
// in the code is one of them, sent from the .Contracts of a sender in its row. Every receiver must also be allowed to
// listen to every sender of that event by the table "Wer auf wessen Integration Events hört" in docs/architecture.md,
// otherwise the catalog would bring in an edge that rule 6 never checked for cycles.
internal static class EventCatalogRule
{
    // The template's example. It goes away with a service's first real entity, so it never gets into the catalog.
    public const string TemplateExample = "ExampleItemCreated";

    private const string TableHeader = "| Ereignis | Sender | Auslöser | Zweck | Empfänger | Empfänger im MVP-Plus |";

    // A tenth event needs a decision in docs/spec/non-goals.md first, not just a row.
    public static IReadOnlyList<string> Nine { get; } =
    [
        "UserDeleted", "DocumentAnalyzed", "DocumentDeleted", "TopicsChanged", "ItemsGenerated", "ItemFlagged",
        "AnswerRecorded", "CardReviewed", "SessionCompleted",
    ];

    // Event and senders for each row of the catalog. Empty if the table is missing.
    public static IReadOnlyList<(string Event, IReadOnlyList<string> Senders)> Events(string markdown) =>
        Rows(markdown).Select(cells => (cells[1].Trim('`'), (IReadOnlyList<string>)[.. Services(cells[2])])).ToList();

    // Event, consumer and sender for each row of the catalog, MVP and MVP-Plus receivers alike. Only "–" means no
    // service, anything else counts as a name, so an annotated cell shows up as a pair the table doesn't know.
    // Empty if the table is missing.
    public static IReadOnlyList<(string Event, string Consumer, string Sender)> Pairs(string markdown) =>
        Rows(markdown)
            .SelectMany(cells => Services(cells[5]).Concat(Services(cells[6]))
                .SelectMany(consumer => Services(cells[2]).Select(sender => (cells[1].Trim('`'), consumer, sender))))
            .ToList();

    public static IEnumerable<string> Violations(
        IEnumerable<(string Event, string Consumer, string Sender)> pairs, IReadOnlyCollection<(string From, string To)> allowed) =>
        pairs.Where(pair => !allowed.Contains((pair.Consumer, pair.Sender)))
            .Select(pair => $"{pair.Consumer} receives {pair.Event} from {pair.Sender}, but the event table in docs/architecture.md doesn't let it listen to {pair.Sender}");

    // Every event of the catalog beyond the nine, every one of the nine it lacks and every event with more than one row.
    public static IEnumerable<string> NotTheNine(IEnumerable<string> events)
    {
        var listed = events.ToList();
        return listed.Except(Nine).Select(name => $"{name} would be a tenth event")
            .Concat(Nine.Except(listed).Select(name => $"{name} is missing"))
            .Concat(listed.GroupBy(name => name).Where(rows => rows.Count() > 1).Select(rows => $"{rows.Key} has {rows.Count()} rows"));
    }

    // Every integration event among the types that the catalog doesn't know, or doesn't know from where it is.
    public static IEnumerable<string> Undeclared(IEnumerable<Type> types, IEnumerable<(string Event, IReadOnlyList<string> Senders)> catalog)
    {
        var senders = catalog.DistinctBy(entry => entry.Event).ToDictionary(entry => entry.Event, entry => entry.Senders);
        foreach (var type in EventsIn(types).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var sender = ServiceOf(type);
            if (sender is null)
            {
                yield return $"{type.FullName} is an integration event outside Edvaniq.Services.<Sender>.Contracts";
            }
            else if (!senders.TryGetValue(type.Name, out var allowed))
            {
                yield return $"{sender} sends {type.Name}, which is not in docs/spec/events.md";
            }
            else if (!allowed.Contains(sender))
            {
                yield return $"{sender} sends {type.Name}, but the catalog names only {string.Join(", ", allowed)} as its sender";
            }
        }
    }

    // The integration events among the types, without the template's example.
    public static IEnumerable<Type> EventsIn(IEnumerable<Type> types) =>
        types.Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false }
            && type.Name != TemplateExample);

    // All types of the service projects the tests reference.
    public static IEnumerable<Type> TypesOf(IEnumerable<ServiceProject> projects) =>
        projects.Select(project => project.Load()).OfType<Assembly>().SelectMany(assembly => assembly.GetTypes());

    public static IEnumerable<string> Unreferenced(IEnumerable<ServiceProject> projects) =>
        projects.Where(project => project.Load() is null)
            .Select(project => $"{project.Name} is not referenced by Edvaniq.ArchitectureTests, so its events stay unchecked");

    // The service whose .Contracts hold the type, as the broker routes it, or null outside every .Contracts.
    public static string? ServiceOf(Type type) =>
        type.Namespace?.Split('.') is ["Edvaniq", "Services", var service, "Contracts", ..] ? service : null;

    private static IEnumerable<string[]> Rows(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.Trim()).ToList();
        var header = lines.IndexOf(TableHeader);
        return header < 0 ? [] : lines.Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries));
    }

    private static IEnumerable<string> Services(string cell) =>
        cell == "–" ? [] : cell.Split(',', StringSplitOptions.TrimEntries);
}