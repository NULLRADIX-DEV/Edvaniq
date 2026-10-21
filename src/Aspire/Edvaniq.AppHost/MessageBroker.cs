namespace Edvaniq.AppHost;

internal static class MessageBrokerExtensions
{
    // The service gets the address of the broker under the name it reads (ConnectionStrings:nats) and starts after it.
    // Only services from the template use it. Their migration step does not, it never sends or receives.
    public static IResourceBuilder<ProjectResource> WithMessaging(
        this IResourceBuilder<ProjectResource> project, IResourceBuilder<NatsServerResource> nats) =>
        project.WithReference(nats).WaitFor(nats);
}