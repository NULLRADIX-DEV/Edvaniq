namespace Edvaniq.ArchitectureTests;

// Rule 6 in docs/architecture.md: integration events form no cycle. An edge leads from a service to each other service
// whose events it listens to: in the code to each foreign .Contracts it references, in the docs to each service in its
// row of the table "Wer auf wessen Integration Events hört". For every service on a cycle the shortest cycle through it
// is reported, starting at its alphabetically first service, so each cycle shows up once.
internal static class CycleRule
{
    private const string TableHeader = "| Consumer | konsumiert von |";

    public static IEnumerable<string> Violations(IEnumerable<ServiceProject> projects) =>
        Cycles(projects.SelectMany(project => project.References
            .Select(ServiceProject.Parse)
            .Where(reference => reference is { Layer: "Contracts" } && reference.Value.Service != project.Service)
            .Select(reference => (project.Service, reference!.Value.Service))));

    public static IEnumerable<string> Cycles(IEnumerable<(string From, string To)> edges)
    {
        var next = edges.Distinct().ToLookup(edge => edge.From, edge => edge.To);
        var cycles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var start in next.Select(group => group.Key))
        {
            if (ShortestCycle(start, next) is { } cycle)
            {
                var first = cycle.IndexOf(cycle.Min(StringComparer.Ordinal)!);
                var rotated = cycle[first..].Concat(cycle[..first]).ToList();
                cycles.Add(string.Join(" -> ", rotated.Append(rotated[0])));
            }
        }

        return cycles;
    }

    // Consumer -> producer for each row of the table, empty if the table is missing.
    public static IReadOnlyList<(string From, string To)> Table(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.Trim()).ToList();
        var header = lines.IndexOf(TableHeader);
        return header < 0 ? [] : lines.Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .SelectMany(cells => cells[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(producer => (cells[1], producer)))
            .ToList();
    }

    // Breadth-first from start until an edge leads back to it: the services of the cycle, without start at the end.
    private static List<string>? ShortestCycle(string start, ILookup<string, string> next)
    {
        var previous = new Dictionary<string, string>();
        var queue = new Queue<string>([start]);
        while (queue.TryDequeue(out var node))
        {
            foreach (var target in next[node].Order(StringComparer.Ordinal))
            {
                if (target == start)
                {
                    var cycle = new List<string>();
                    for (var step = node; step != start; step = previous[step])
                    {
                        cycle.Insert(0, step);
                    }

                    cycle.Insert(0, start);
                    return cycle;
                }

                if (previous.TryAdd(target, node))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return null;
    }
}