using System.Text.Json;

using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.UnitTests;

// The schema sent along with a prompt asks for the same form as the example in the prompt, field by field, and is strict:
// every field required, no other allowed.
public class SchemaTests
{
    public static TheoryData<string, string> Tasks => new()
    {
        { TopicAnalysis.Schema, TopicAnalysis.System(LearnerProfile.Default) },
        { QuestionGeneration.Schema, QuestionGeneration.System(LearnerProfile.Default) },
    };

    [Theory]
    [MemberData(nameof(Tasks))]
    public void Schema_MatchesTheExampleOfThePrompt(string schema, string system)
    {
        using var json = JsonDocument.Parse(schema);
        using var example = JsonDocument.Parse(system.ReplaceLineEndings("\n").TrimEnd().Split('\n')[^1]);

        AssertMatches(json.RootElement, example.RootElement, "$");
    }

    private static void AssertMatches(JsonElement schema, JsonElement value, string path)
    {
        switch (schema.GetProperty("type").GetString())
        {
            case "object":
                Assert.False(schema.GetProperty("additionalProperties").GetBoolean(), path);
                var required = schema.GetProperty("required").EnumerateArray().Select(field => field.GetString()!).Order().ToList();
                Assert.Equal(required, schema.GetProperty("properties").EnumerateObject().Select(field => field.Name).Order().ToList());
                Assert.Equal(required, value.EnumerateObject().Select(field => field.Name).Order().ToList());
                foreach (var field in schema.GetProperty("properties").EnumerateObject())
                {
                    AssertMatches(field.Value, value.GetProperty(field.Name), $"{path}.{field.Name}");
                }

                break;
            case "array":
                Assert.Equal(JsonValueKind.Array, value.ValueKind);
                foreach (var item in value.EnumerateArray())
                {
                    AssertMatches(schema.GetProperty("items"), item, $"{path}[]");
                }

                break;
            case "string":
                Assert.Equal(JsonValueKind.String, value.ValueKind);
                break;
            case "integer":
                Assert.True(value.TryGetInt32(out _), path);
                break;
            default:
                Assert.Fail($"Unexpected type at {path}");
                break;
        }
    }
}