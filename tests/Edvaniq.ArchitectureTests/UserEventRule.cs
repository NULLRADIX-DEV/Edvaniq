using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.ArchitectureTests;

// #944: an event about a user's data is an IUserEvent, so consumers confirm it without effect once the user is deleted.
// Without the interface a late event would bring the user's data back. Only Identity's UserDeleted is none, it is what
// marks the user as deleted.
internal static class UserEventRule
{
    private static readonly string[] UserFields = ["UserId", "OwnerId"];

    public static IEnumerable<string> Violations(IEnumerable<Type> types) =>
        types.Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(Check);

    private static IEnumerable<string> Check(Type type)
    {
        var isUserEvent = typeof(IUserEvent).IsAssignableFrom(type);
        if (IsUserDeleted(type))
        {
            if (isUserEvent)
            {
                yield return $"{type.FullName} is an IUserEvent, so a deleted user would skip their own deletion";
            }
        }
        else if (!isUserEvent && type.GetProperties().Any(property => UserFields.Contains(property.Name)))
        {
            yield return $"{type.FullName} names a user but is no IUserEvent, a late one would bring a deleted user's data back";
        }
    }

    private static bool IsUserDeleted(Type type) => type.FullName == "Edvaniq.Services.Identity.Contracts.UserDeleted";
}