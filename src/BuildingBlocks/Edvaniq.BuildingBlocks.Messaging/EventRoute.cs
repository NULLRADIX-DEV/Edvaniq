namespace Edvaniq.BuildingBlocks.Messaging;

// Where an integration event goes, taken from its type. Every service has one stream, named like the service in upper
// case, and every event of it has a subject below: Edvaniq.Services.Planning.Contracts.ExampleItemCreated goes to the
// stream PLANNING with the subject planning.exampleitemcreated. A consumer finds the stream of an event the same way, so
// it needs nothing but the type.
public sealed record EventRoute(string Service, string Stream, string Subject)
{
    public static EventRoute For(Type eventType)
    {
        if (eventType.Namespace?.Split('.') is not ["Edvaniq", "Services", var service, "Contracts", ..])
        {
            throw new InvalidOperationException(
                $"{eventType.FullName} is no integration event of a service. It belongs in Edvaniq.Services.<Service>.Contracts.");
        }

        return new EventRoute(
            service.ToLowerInvariant(),
            StreamOf(service),
            $"{service.ToLowerInvariant()}.{eventType.Name.ToLowerInvariant()}");
    }

    public static string StreamOf(string service) => service.ToUpperInvariant();

    // All subjects of a service, the filter of its stream.
    public static string SubjectsOf(string service) => $"{service.ToLowerInvariant()}.>";
}