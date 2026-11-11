using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.Application;

namespace Edvaniq.ArchitectureTests;

// Every consumer of an integration event has an integration test that delivers the same event twice, once under the
// same message id and once under a new one. The table "Doppelte Zustellung" in docs/service-internals.md names that test
// for each consumer, so a new handler without a row, a row without a handler and a row naming a missing test show up here.
internal static class DuplicateDeliveryRule
{
    private const string TableHeader = "| Service | Ereignis | Test |";

    // Service and event of every handler among the types, without the template's example.
    public static IEnumerable<(string Service, string Event)> Consumers(IEnumerable<Type> types) =>
        types.Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
                .Select(contract => (Service: ServiceOf(type), Event: contract.GetGenericArguments()[0].Name)))
            .Where(consumer => consumer.Service is not null && consumer.Event != EventCatalogRule.TemplateExample)
            .Select(consumer => (consumer.Service!, consumer.Event))
            .Distinct();

    // Service, event and the tests named as Class.Method for each row. Empty if the table is missing.
    public static IReadOnlyList<(string Service, string Event, IReadOnlyList<string> Tests)> Rows(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.Trim()).ToList();
        var header = lines.IndexOf(TableHeader);
        return header < 0 ? [] : lines.Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Select(cells => (cells[1], cells[2].Trim('`'),
                (IReadOnlyList<string>)[.. cells[3].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(test => test.Trim('`'))]))
            .ToList();
    }

    public static IEnumerable<string> Violations(
        IEnumerable<(string Service, string Event)> consumers,
        IReadOnlyList<(string Service, string Event, IReadOnlyList<string> Tests)> rows,
        Func<string, string, bool> testExists)
    {
        var known = consumers.ToList();
        var listed = rows.Select(row => (row.Service, row.Event)).ToList();

        foreach (var (service, handled) in known.Where(consumer => !listed.Contains(consumer)))
        {
            yield return $"{service} handles {handled}, but no row names the test for its double delivery";
        }

        foreach (var row in rows)
        {
            if (!known.Contains((row.Service, row.Event)))
            {
                yield return $"The row for {Label(row)} names no handler: {row.Service} doesn't handle {row.Event}";
            }
            else if (row.Tests.Count == 0)
            {
                yield return $"The row for {Label(row)} names no test";
            }

            foreach (var test in row.Tests.Where(test => !testExists(row.Service, test)))
            {
                yield return $"The row for {Label(row)} names {test}, which is no test in tests/Services/{row.Service}";
            }
        }

        foreach (var twice in listed.GroupBy(row => row).Where(group => group.Count() > 1))
        {
            yield return $"{twice.Key.Service} and {twice.Key.Event} have {twice.Count()} rows";
        }

        static string Label((string Service, string Event, IReadOnlyList<string> Tests) row) => $"{row.Event} in {row.Service}";
    }

    // Whether tests/Services/<service> has an integration test Class.Method, in the file of its class.
    public static bool TestExists(string root, string service, string test)
    {
        var parts = test.Split('.');
        var folder = Path.Combine(root, "tests", "Services", service);
        if (parts.Length != 2 || !Directory.Exists(folder))
        {
            return false;
        }

        var method = new Regex($@"\bTask\s+{Regex.Escape(parts[1])}\s*\(");
        return Directory.EnumerateDirectories(folder, "*.IntegrationTests")
            .Select(project => Path.Combine(project, parts[0] + ".cs"))
            .Any(file => File.Exists(file) && method.IsMatch(File.ReadAllText(file)));
    }

    private static string? ServiceOf(Type type) =>
        type.Namespace?.Split('.') is ["Edvaniq", "Services", var service, ..] ? service : null;
}