using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;

namespace Edvaniq.ArchitectureTests;

// UserDeleted removes everything that belongs to the user (#201): the remover of every service finds each IOwnedByUser
// entity itself. So every entity in a DbContext of a service is IOwnedByUser, or it is on the list below with the reason
// it holds nothing of a user, or is removed otherwise. A new table without an owner fails here before it can outlive a
// deleted user. Types from BuildingBlocks (inbox and outbox) are their own business.
public class UserDataTests
{
    private static readonly Dictionary<Type, string> Exempt = new()
    {
        [typeof(PingRecord)] = "the ping of the walking skeleton, written without a user",
        [typeof(AiUsageDay)] = "AI cost as daily sums without the user, UserDeleted turns the user's rows into these",
        [typeof(Account)] = "Identity deletes the account itself and sends UserDeleted (AccountDeletion)",
    };

    [Fact]
    public void EveryEntity_BelongsToAUser_OrSaysWhyNot() =>
        Rules.AssertNone(
            ServiceProject.ReadAll(Repository.Root).SelectMany(project => project.Load() is { } assembly
                ? Violations(assembly.GetTypes(), Exempt.Keys)
                : [$"{project.Name} is not referenced by Edvaniq.ArchitectureTests, so its DbContexts stay unchecked"]),
            "User data", "docs/service-internals.md");

    [Fact]
    public void EntityWithoutOwner_IsReported() =>
        Assert.Equal(
            ["PlanningDbContext maps PingRecord, which belongs to no user"],
            Violations(typeof(PlanningDbContext).Assembly.GetTypes(), []));

    [Fact]
    public void EveryExemption_IsStillAnEntity() =>
        Assert.All(Exempt.Keys, type => Assert.False(typeof(IOwnedByUser).IsAssignableFrom(type), $"{type.Name} has an owner now, take it off the list."));

    private static IEnumerable<string> Violations(IEnumerable<Type> types, IEnumerable<Type> exempt)
    {
        var (_, contexts) = DesignTimeContexts.Find(types);
        var allowed = exempt.ToHashSet();
        var violations = new List<string>();
        foreach (var create in contexts)
        {
            using var context = create();
            violations.AddRange(context.Model.GetEntityTypes()
                .Where(entity => !entity.IsOwned())
                .Select(entity => entity.ClrType)
                .Where(type => type.Namespace?.StartsWith("Edvaniq.BuildingBlocks", StringComparison.Ordinal) != true)
                .Where(type => !typeof(IOwnedByUser).IsAssignableFrom(type) && !allowed.Contains(type))
                .Select(type => $"{context.GetType().Name} maps {type.Name}, which belongs to no user"));
        }

        return violations.Order(StringComparer.Ordinal);
    }
}