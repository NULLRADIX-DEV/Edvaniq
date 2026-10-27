using System.Reflection;

namespace Edvaniq.ArchitectureTests;

// Events change only additively (docs/spec/events.md). EventContracts.txt keeps every field of every event that may
// have gone out, one line each as "<Service>.<Type>.<Field>: <type>". Records inside an event count with their own
// fields, an enum with each value and its number, because the number is what goes over the wire. The code must still
// have every field of the file with the same type, and a field the file doesn't know yet has to be added there.
internal static class EventContractRule
{
    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(bool)] = "bool",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
    };

    // Field and type for each line, without blank lines and # comments.
    public static IReadOnlyDictionary<string, string> Parse(string text) =>
        text.Split('\n').Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split(": ", 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    // Field and type of every property of the events and of the records and enums inside them, sorted as in
    // EventContracts.txt. The type of an enum value is "= <number>".
    public static IReadOnlyDictionary<string, string> Fields(IEnumerable<Type> events)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();
        var pending = new Queue<Type>(events);
        var seen = pending.ToHashSet();
        while (pending.TryDequeue(out var type))
        {
            var prefix = $"{EventCatalogRule.ServiceOf(type)}.{type.Name}.";
            foreach (var value in type.IsEnum ? type.GetFields(BindingFlags.Public | BindingFlags.Static) : [])
            {
                fields[prefix + value.Name] = $"= {value.GetRawConstantValue()}";
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                fields[prefix + property.Name] = Name(nullability.Create(property));
                foreach (var part in ProjectionRule.Parts(property.PropertyType)
                    .Where(part => EventCatalogRule.ServiceOf(part) is not null && seen.Add(part)))
                {
                    pending.Enqueue(part);
                }
            }
        }

        return fields;
    }

    // Every field of the file that the code lost or changed, and every field of the code that the file doesn't have yet.
    public static IEnumerable<string> Violations(IReadOnlyDictionary<string, string> shipped, IReadOnlyDictionary<string, string> current)
    {
        foreach (var (field, type) in shipped)
        {
            if (!current.TryGetValue(field, out var now))
            {
                yield return $"{field} is gone, receivers and old messages still need it";
            }
            else if (now != type)
            {
                yield return $"{field} changed from {type} to {now}";
            }
        }

        // A type is shipped once one of its fields is in the file. Old messages of it lack a new field, so that one
        // has to be nullable. A new enum value is fine, old messages just never carry it.
        var shippedTypes = shipped.Keys.Select(TypeOf).ToHashSet(StringComparer.Ordinal);
        foreach (var (field, type) in current.Where(field => !shipped.ContainsKey(field.Key)))
        {
            var optional = type.EndsWith('?') || type.StartsWith('=');
            yield return shippedTypes.Contains(TypeOf(field)) && !optional
                ? $"{field}: {type} is new on a shipped type and must be optional"
                : $"{field}: {type} is missing in EventContracts.txt";
        }
    }

    // Identity.UserDeleted for Identity.UserDeleted.UserId.
    private static string TypeOf(string field) => field[..field.LastIndexOf('.')];

    // The type as C# spells it, with its nullability, e.g. string?, Guid? or IReadOnlyList<Guid>.
    private static string Name(NullabilityInfo info)
    {
        if (Nullable.GetUnderlyingType(info.Type) is { } value)
        {
            return Keywords.GetValueOrDefault(value, value.Name) + "?";
        }

        var name = info.Type.IsArray ? Name(info.ElementType!) + "[]"
            : info.Type.IsGenericType ? $"{info.Type.Name[..info.Type.Name.IndexOf('`')]}<{string.Join(", ", info.GenericTypeArguments.Select(Name))}>"
            : Keywords.GetValueOrDefault(info.Type, info.Type.Name);
        return info.ReadState == NullabilityState.Nullable ? name + "?" : name;
    }
}