using Edvaniq.Services.Content.Contracts.Fakes;

namespace Edvaniq.ArchitectureTests;

// Events change only additively (docs/spec/events.md, "Der Vertrag im Code"): the events in the code keep every field
// of EventContracts.txt with its type, and a new field on a type of the file is optional.
public class EventContractTests
{
    [Fact]
    public void EventsInCode_KeepTheShippedFields()
    {
        var events = EventCatalogRule.EventsIn(EventCatalogRule.TypesOf(ServiceProject.ReadAll(Repository.Root)));
        var shipped = EventContractRule.Parse(
            File.ReadAllText(Path.Combine(Repository.Root, "tests", "Edvaniq.ArchitectureTests", "EventContracts.txt")));

        Rules.AssertNone(EventContractRule.Violations(shipped, EventContractRule.Fields(events)), "The additive rule", "docs/spec/events.md");
        Assert.Equal("string", shipped["Identity.UserDeleted.UserId"]);
    }

    [Fact]
    public void Fields_CoverRecordsAndEnumsInsideAnEvent()
    {
        var fields = EventContractRule.Fields([typeof(DocumentShared)]);

        Assert.Equal(
            [
                "Content.DocumentShared.Parts: IReadOnlyList<SharedPart>",
                "Content.DocumentShared.TopicId: Guid?",
                "Content.DocumentShared.UserId: string",
                "Content.DocumentShared.Visibility: Visibility",
                "Content.SharedPart.Note: string?",
                "Content.SharedPart.Pages: int[]",
                "Content.SharedPart.SectionId: Guid",
                "Content.Visibility.Private: = 0",
                "Content.Visibility.Team: = 2",
            ],
            fields.Select(field => $"{field.Key}: {field.Value}"));
    }

    [Fact]
    public void LostOrChangedField_IsReported()
    {
        var shipped = EventContractRule.Parse("""
            # A comment and a blank line

            Identity.UserDeleted.UserId: string
            Content.DocumentShared.TopicId: Guid?
            Content.DocumentShared.Visibility: Visibility
            Content.Visibility.Team: = 2
            """);
        var current = new Dictionary<string, string>
        {
            ["Identity.UserDeleted.UserId"] = "Guid",
            ["Content.DocumentShared.Visibility"] = "Visibility",
            ["Content.Visibility.Team"] = "= 3",
        };

        Assert.Equal(
            [
                "Content.DocumentShared.TopicId is gone, receivers and old messages still need it",
                "Content.Visibility.Team changed from = 2 to = 3",
                "Identity.UserDeleted.UserId changed from string to Guid",
            ],
            EventContractRule.Violations(shipped, current).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NewFieldOnShippedType_MustBeOptional()
    {
        var shipped = EventContractRule.Parse("""
            Identity.UserDeleted.UserId: string
            Content.Visibility.Private: = 0
            """);
        var current = new Dictionary<string, string>
        {
            ["Identity.UserDeleted.UserId"] = "string",
            ["Identity.UserDeleted.Reason"] = "string",
            ["Identity.UserDeleted.DeletedAt"] = "DateTime?",
            ["Identity.UserDeletedLater.UserId"] = "string",
            ["Content.Visibility.Private"] = "= 0",
            ["Content.Visibility.Team"] = "= 2",
        };

        Assert.Equal(
            [
                "Content.Visibility.Team: = 2 is missing in EventContracts.txt",
                "Identity.UserDeleted.DeletedAt: DateTime? is missing in EventContracts.txt",
                "Identity.UserDeleted.Reason: string is new on a shipped type and must be optional",
                "Identity.UserDeletedLater.UserId: string is missing in EventContracts.txt",
            ],
            EventContractRule.Violations(shipped, current).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnchangedFields_AreFine() =>
        Assert.Empty(EventContractRule.Violations(
            EventContractRule.Parse("Identity.UserDeleted.UserId: string"),
            new Dictionary<string, string> { ["Identity.UserDeleted.UserId"] = "string" }));
}