using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application.Fakes;
using Edvaniq.Services.Content.Contracts.Fakes;

namespace Edvaniq.ArchitectureTests;

// An event can arrive twice: as a redelivery under the same message id, which the inbox drops, and as the same event
// under a new message id, which the handler itself must take as a no-op. The table in docs/service-internals.md names
// for every consumer the integration test that shows both, and this checks that the table and the handlers agree.
public class DuplicateDeliveryTests
{
    private const string Document = "docs/service-internals.md";

    private static string Internals => File.ReadAllText(Path.Combine(Repository.Root, "docs", "service-internals.md"));

    [Fact]
    public void EveryConsumer_HasADuplicateDeliveryTest()
    {
        var projects = ServiceProject.ReadAll(Repository.Root);
        var consumers = DuplicateDeliveryRule.Consumers(EventCatalogRule.TypesOf(projects)).ToList();

        var violations = EventCatalogRule.Unreferenced(projects).Concat(DuplicateDeliveryRule.Violations(
            consumers, DuplicateDeliveryRule.Rows(Internals),
            (service, test) => DuplicateDeliveryRule.TestExists(Repository.Root, service, test)));

        Rules.AssertNone(violations, "Double delivery", Document);
        Assert.Contains(("Knowledge", "DocumentAnalyzed"), consumers);
        Assert.DoesNotContain(("Planning", EventCatalogRule.TemplateExample), consumers);
    }

    [Fact]
    public void Handlers_AreFoundWithoutTheExampleAndAbstractOnes()
    {
        var types = new[]
        {
            typeof(DocumentSharedHandler), typeof(UserDeletedHandler), typeof(ExampleItemCreatedHandler), typeof(HandlerBase),
            typeof(LooseHandler),
        };

        Assert.Equal([("Content", "DocumentShared"), ("Content", "UserDeleted")], DuplicateDeliveryRule.Consumers(types));
    }

    [Fact]
    public void MissingRow_UnknownHandler_MissingTest_AndTwoRows_AreReported()
    {
        var rows = DuplicateDeliveryRule.Rows("""
            | Service | Ereignis | Test |
            |---|---|---|
            | Content | `UserDeleted` | `UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm`, `UserDeletionTests.Gone` |
            | Content | `TopicsChanged` | `KnownTopicTests.StateDeliveredTwice` |
            | Content | `UserDeleted` | |

            | Andere Tabelle | danach |
            """);
        var consumers = new[] { ("Content", "DocumentShared"), ("Content", "UserDeleted") };

        var violations = DuplicateDeliveryRule.Violations(
            consumers, rows, (_, test) => test != "UserDeletionTests.Gone" && test != "KnownTopicTests.StateDeliveredTwice");

        Assert.Equal(
            [
                "Content handles DocumentShared, but no row names the test for its double delivery",
                "The row for UserDeleted in Content names UserDeletionTests.Gone, which is no test in tests/Services/Content",
                "The row for TopicsChanged in Content names no handler: Content doesn't handle TopicsChanged",
                "The row for TopicsChanged in Content names KnownTopicTests.StateDeliveredTwice, which is no test in tests/Services/Content",
                "The row for UserDeleted in Content names no test",
                "Content and UserDeleted have 2 rows",
            ],
            violations);
    }

    [Fact]
    public void TestExists_OnlyForAMethodOfTheClassInTheIntegrationTests()
    {
        using var services = new FakeServices();
        var folder = Directory.CreateDirectory(Path.Combine(services.Root, "tests", "Services", "Content", "Edvaniq.Services.Content.IntegrationTests")).FullName;
        File.WriteAllText(Path.Combine(folder, "UserDeletionTests.cs"), "public async Task DeletionDeliveredTwice_OrSentTwice_DoesNoHarm() { }");
        var units = Directory.CreateDirectory(Path.Combine(services.Root, "tests", "Services", "Content", "Edvaniq.Services.Content.UnitTests")).FullName;
        File.WriteAllText(Path.Combine(units, "HandlerTests.cs"), "public async Task SameEventTwice() { }");

        Assert.True(DuplicateDeliveryRule.TestExists(services.Root, "Content", "UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm"));
        Assert.False(DuplicateDeliveryRule.TestExists(services.Root, "Content", "UserDeletionTests.DeletionDeliveredTwice"));
        Assert.False(DuplicateDeliveryRule.TestExists(services.Root, "Content", "HandlerTests.SameEventTwice"));
        Assert.False(DuplicateDeliveryRule.TestExists(services.Root, "Planning", "UserDeletionTests.DeletionDeliveredTwice_OrSentTwice_DoesNoHarm"));
        Assert.False(DuplicateDeliveryRule.TestExists(services.Root, "Content", "DeletionDeliveredTwice_OrSentTwice_DoesNoHarm"));
    }

    // A handler outside every service.
    private sealed class LooseHandler : IIntegrationEventHandler<DocumentShared>
    {
        public Task HandleAsync(DocumentShared integrationEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}