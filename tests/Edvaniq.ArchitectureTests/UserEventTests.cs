using Edvaniq.Services.Content.Contracts.Fakes;
using Edvaniq.Services.Planning.Contracts;

using IdentityUserDeleted = Edvaniq.Services.Identity.Contracts.UserDeleted;

namespace Edvaniq.ArchitectureTests;

public class UserEventTests
{
    [Fact]
    public void EveryEventAboutAUser_IsAUserEvent()
    {
        var projects = ServiceProject.ReadAll(Repository.Root);

        Rules.AssertNone(
            EventCatalogRule.Unreferenced(projects).Concat(UserEventRule.Violations(EventCatalogRule.TypesOf(projects))),
            "Deleted users", "docs/service-internals.md");
    }

    // DocumentShared and Content's UserDeleted name a user and lack the interface, Identity's UserDeleted may.
    [Fact]
    public void EventAboutAUserWithoutTheInterface_IsReported() =>
        Assert.Equal(
            [
                "Edvaniq.Services.Content.Contracts.Fakes.DocumentShared names a user but is no IUserEvent, a late one would bring a deleted user's data back",
                "Edvaniq.Services.Content.Contracts.Fakes.UserDeleted names a user but is no IUserEvent, a late one would bring a deleted user's data back",
            ],
            UserEventRule.Violations([typeof(DocumentShared), typeof(SharedPart), typeof(UserDeleted), typeof(IdentityUserDeleted), typeof(ExampleItemCreated)]));
}