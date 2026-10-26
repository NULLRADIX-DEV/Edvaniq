namespace Edvaniq.ArchitectureTests;

// Rule 6 in docs/architecture.md: every sender and receiver pair in the event catalog docs/spec/events.md is a row of
// the table "Wer auf wessen Integration Events hört", so the catalog stays free of cycles too.
public class EventCatalogTests
{
    [Fact]
    public void CatalogPairs_AreInEventTable()
    {
        var pairs = EventCatalogRule.Pairs(File.ReadAllText(Path.Combine(Repository.Root, "docs", "spec", "events.md")));
        var allowed = CycleRule.Table(File.ReadAllText(Path.Combine(Repository.Root, "docs", "architecture.md")));

        Rules.AssertNone(EventCatalogRule.Violations(pairs, allowed), "Rule 6", "docs/spec/events.md");
        Assert.Contains(("UserDeleted", "Planning", "Identity"), pairs);
    }

    [Fact]
    public void PairMissingFromTable_IsReported()
    {
        var pairs = EventCatalogRule.Pairs("""
            | Ereignis | Sender | Auslöser | Empfänger | Empfänger im MVP-Plus |
            |---|---|---|---|---|
            | `ItemFlagged` | Assessment, Flashcards | Ein Nutzer meldet ein Item. | LearningEngine | – |
            | `SessionCompleted` | Planning | Eine Session endet. | LearningEngine | Gamification |
            | `CardReviewed` | Flashcards | Ein Nutzer bewertet eine Karte. | LearningEngine (ab M4) | – |

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
}