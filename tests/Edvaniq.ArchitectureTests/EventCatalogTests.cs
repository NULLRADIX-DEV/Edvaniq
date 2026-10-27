using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Content.Contracts.Fakes;
using Edvaniq.Services.Planning.Contracts;

using IdentityUserDeleted = Edvaniq.Services.Identity.Contracts.UserDeleted;

namespace Edvaniq.ArchitectureTests;

// The event catalog docs/spec/events.md has exactly the nine events of the MVP, and no service sends one outside it.
// Rule 6 in docs/architecture.md: every sender and receiver pair in the catalog is a row of the table "Wer auf wessen
// Integration Events hört", so the catalog stays free of cycles too.
public class EventCatalogTests
{
    private const string Document = "docs/spec/events.md";

    private static string Catalog => File.ReadAllText(Path.Combine(Repository.Root, "docs", "spec", "events.md"));

    [Fact]
    public void Catalog_HasTheNineEvents()
    {
        var events = EventCatalogRule.Events(Catalog);

        Rules.AssertNone(EventCatalogRule.NotTheNine(events.Select(entry => entry.Event)), "The event catalog", Document);
        Assert.Equal(["Assessment", "Flashcards"], events.Single(entry => entry.Event == "ItemFlagged").Senders);
    }

    [Fact]
    public void TenthOrMissingEvent_IsReported()
    {
        var events = EventCatalogRule.Nine.Where(name => name != "CardReviewed").Append("DocumentShared").Append("UserDeleted");

        Assert.Equal(
            ["DocumentShared would be a tenth event", "CardReviewed is missing", "UserDeleted has 2 rows"],
            EventCatalogRule.NotTheNine(events));
    }

    [Fact]
    public void EventsInCode_AreInCatalog()
    {
        var projects = ServiceProject.ReadAll(Repository.Root);
        var violations = EventCatalogRule.Unreferenced(projects)
            .Concat(EventCatalogRule.Undeclared(EventCatalogRule.TypesOf(projects), EventCatalogRule.Events(Catalog)));

        Rules.AssertNone(violations, "The event catalog", Document);
    }

    [Fact]
    public void EventOutsideCatalog_IsReported()
    {
        var types = new[]
        {
            typeof(DocumentShared), typeof(SharedPart), typeof(UserDeleted), typeof(LooseEvent),
            typeof(IdentityUserDeleted), typeof(ExampleItemCreated),
        };

        Assert.Equal(
            [
                "Edvaniq.ArchitectureTests.EventCatalogTests+LooseEvent is an integration event outside Edvaniq.Services.<Sender>.Contracts",
                "Content sends DocumentShared, which is not in docs/spec/events.md",
                "Content sends UserDeleted, but the catalog names only Identity as its sender",
            ],
            EventCatalogRule.Undeclared(types, EventCatalogRule.Events(Catalog)));
    }

    [Fact]
    public void CatalogPairs_AreInEventTable()
    {
        var pairs = EventCatalogRule.Pairs(Catalog);
        var allowed = CycleRule.Table(File.ReadAllText(Path.Combine(Repository.Root, "docs", "architecture.md")));

        Rules.AssertNone(EventCatalogRule.Violations(pairs, allowed), "Rule 6", Document);
        Assert.Contains(("UserDeleted", "Planning", "Identity"), pairs);
    }

    [Fact]
    public void PairMissingFromTable_IsReported()
    {
        var pairs = EventCatalogRule.Pairs("""
            | Ereignis | Sender | Auslöser | Zweck | Empfänger | Empfänger im MVP-Plus |
            |---|---|---|---|---|---|
            | `ItemFlagged` | Assessment, Flashcards | Ein Nutzer meldet ein Item. | Raus aus der Mastery. | LearningEngine | – |
            | `SessionCompleted` | Planning | Eine Session endet. | Themen eingeführt. | LearningEngine | Gamification |
            | `CardReviewed` | Flashcards | Ein Nutzer bewertet eine Karte. | Neuer Lern-Nachweis. | LearningEngine (ab M4) | – |

            | Andere Tabelle | danach |
            """);

        var violations = EventCatalogRule.Violations(pairs, [("LearningEngine", "Assessment"), ("LearningEngine", "Planning")]);

        Assert.Equal(
            [
                "LearningEngine receives ItemFlagged from Flashcards, but the event table in docs/architecture.md doesn't let it listen to Flashcards",
                "Gamification receives SessionCompleted from Planning, but the event table in docs/architecture.md doesn't let it listen to Planning",
                "LearningEngine (ab M4) receives CardReviewed from Flashcards, but the event table in docs/architecture.md doesn't let it listen to Flashcards",
            ],
            violations);
    }

    // An event outside every .Contracts, so no broker route either.
    private sealed record LooseEvent(string UserId) : IIntegrationEvent;
}