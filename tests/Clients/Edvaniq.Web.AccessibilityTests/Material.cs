using System.Text.Json;

using Microsoft.Playwright;

namespace Edvaniq.Web.AccessibilityTests;

// A text and its topics as Content and Knowledge answer them, for the stub gateway and for answers a test gives the page
// itself. One topic has a long word without a break, as a heading from the analysis might.
internal static class Material
{
    public static readonly Guid DocumentId = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");

    public const string LongWord = "Donaudampfschifffahrtselektrizitätenhauptbetriebswerkbauunterbeamtengesellschaft";

    public static readonly object[] Topics =
    [
        new
        {
            id = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50"),
            learningGoalId = Guid.Empty,
            name = "Photosynthese",
            documentId = DocumentId,
            position = 0,
            subtopics = new[]
            {
                new { id = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"), name = "Lichtreaktion", documentId = DocumentId, position = 1 },
                new { id = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e"), name = LongWord, documentId = DocumentId, position = 2 },
            },
        },
    ];

    public static object Document(string status, string? failureReason = null) => new
    {
        id = DocumentId,
        title = "Skript Biologie",
        status,
        failureReason,
        createdAt = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc),
    };

    // Answers the page's calls to Content and Knowledge in the browser. Each call takes the next answer of its list and
    // the last one stays. The web host and its routes are tested elsewhere, here only the page counts.
    public static async Task AnswerAsync(IPage page, IReadOnlyList<object> documents, IReadOnlyList<object> topics, object? pasted = null)
    {
        var documentCalls = 0;
        var topicCalls = 0;
        await page.RouteAsync("**/api/content/documents", route =>
            route.Request.Method == "POST"
                ? FulfillAsync(route, pasted ?? Document("Uploaded"))
                : FulfillAsync(route, documents[Math.Min(Interlocked.Increment(ref documentCalls) - 1, documents.Count - 1)]));
        await page.RouteAsync("**/api/knowledge/topics", route =>
            FulfillAsync(route, topics[Math.Min(Interlocked.Increment(ref topicCalls) - 1, topics.Count - 1)]));
    }

    public static readonly Guid TopicId = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");

    // A question of the topic as Assessment asks it, or 409 while there are none. The answer is graded with the given
    // option against the second one being right.
    public static async Task AnswerQuizAsync(IPage page, bool ready = true)
    {
        var attempt = Guid.Parse("8c9d0e1f-2a3b-4c4d-9e5f-6a7b8c9d0e1f");
        await AnswerAsync(page, documents: [new[] { Document("Ready") }], topics: [Topics]);
        await page.RouteAsync("**/api/assessment/attempts", route => ready
            ? FulfillAsync(route, new
            {
                id = attempt,
                topicId = TopicId,
                questionId = Guid.NewGuid(),
                text = $"Wo findet die Lichtreaktion statt? ({LongWord})",
                options = new[] { "Im Zellkern", "In den Thylakoiden", LongWord },
                difficulty = 1,
            }, 201)
            : route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 409,
                ContentType = "application/problem+json",
                Body = """{"status":409,"title":"Zu diesem Thema gibt es noch keine Fragen."}""",
            }));
        await page.RouteAsync("**/api/assessment/attempts/*/answer", route =>
        {
            var option = JsonDocument.Parse(route.Request.PostData!).RootElement.GetProperty("option").GetInt32();
            return FulfillAsync(route, new
            {
                id = attempt,
                topicId = TopicId,
                questionId = Guid.NewGuid(),
                option,
                correct = option == 1,
                answer = 1,
                explanation = "Die Lichtreaktion läuft an den Membranen der Thylakoide ab.",
                responseTimeMs = 1000,
                answeredAt = DateTime.UtcNow,
            });
        });
    }

    private static Task FulfillAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        });
}