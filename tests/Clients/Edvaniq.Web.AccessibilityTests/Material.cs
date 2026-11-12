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

    public static object Document(string status, string? failureReason = null, string title = "Skript Biologie") => new
    {
        id = DocumentId,
        title,
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

    // The attempt every question of AnswerQuizAsync belongs to, so also the answer's id at LearningEngine.
    public static readonly Guid AttemptId = Guid.Parse("8c9d0e1f-2a3b-4c4d-9e5f-6a7b8c9d0e1f");

    // LearningEngine's mastery of the topic, with the answer of AnswerQuizAsync once arrived returns true.
    public static Task AnswerMasteryAsync(IPage page, Func<bool> arrived) =>
        page.RouteAsync("**/api/learningengine/mastery", route => FulfillAsync(route, new
        {
            ruleVersion = 2,
            topics = new[]
            {
                arrived()
                    ? new { topicId = TopicId, mastery = 62, state = "building", lastAnswerId = (Guid?)AttemptId }
                    : new { topicId = TopicId, mastery = 50, state = "new", lastAnswerId = (Guid?)null },
            },
        }));

    // Today's plan as Planning answers it: a review of the topic and learn time for both subtopics, one of them the
    // long word. Without blocks it says why with emptyReason.
    public static object Plan(string? emptyReason = null, bool masteryAvailable = true) => new
    {
        studyDay = new DateOnly(2026, 11, 11),
        learningGoalId = Guid.Empty,
        blocks = emptyReason is not null
            ? []
            : new[]
            {
                new { topicId = TopicId, minutes = 10, kind = "review" },
                new { topicId = Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"), minutes = 20, kind = "learn" },
                new { topicId = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e"), minutes = 15, kind = "learn" },
            },
        emptyReason,
        masteryAvailable,
        shortenedTopicIds = emptyReason is not null ? Array.Empty<Guid>() : [Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e")],
        questionPoolsAvailable = true,
        notReadyTopicIds = Array.Empty<Guid>(),
    };

    // Answers the plan and the topics in the browser, each call takes the next plan of the list and the last one stays.
    public static async Task AnswerPlanAsync(IPage page, params object[] plans)
    {
        var calls = 0;
        await AnswerAsync(page, documents: [new[] { Document("Ready") }], topics: [Topics]);
        await page.RouteAsync("**/api/planning/plan/today", route =>
            FulfillAsync(route, plans[Math.Min(Interlocked.Increment(ref calls) - 1, plans.Length - 1)]));
    }

    // The week as Planning answers it (#379): today the blocks of Plan, tomorrow nothing, the day after one learn block.
    // Without blocks it says why with emptyReason.
    public static object Week(string? emptyReason = null)
    {
        var empty = emptyReason is not null;
        var longWord = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");
        object[] today = empty
            ? []
            :
            [
                new { topicId = TopicId, minutes = 10, kind = "review" },
                new { topicId = longWord, minutes = 15, kind = "learn" },
            ];
        object[] later = empty ? [] : [new { topicId = TopicId, minutes = 30, kind = "learn" }];
        return new
        {
            learningGoalId = Guid.Empty,
            days = new[]
            {
                new { studyDay = new DateOnly(2026, 11, 11), blocks = today },
                new { studyDay = new DateOnly(2026, 11, 12), blocks = Array.Empty<object>() },
                new { studyDay = new DateOnly(2026, 11, 13), blocks = later },
            },
            emptyReason,
            masteryAvailable = true,
            shortenedTopicIds = empty ? Array.Empty<Guid>() : [longWord],
            questionPoolsAvailable = true,
            notReadyTopicIds = Array.Empty<Guid>(),
        };
    }

    public static async Task AnswerWeekAsync(IPage page, object week)
    {
        await AnswerAsync(page, documents: [new[] { Document("Ready") }], topics: [Topics]);
        await page.RouteAsync("**/api/planning/plan/week", route => FulfillAsync(route, week));
    }

    // A question of the topic as Assessment asks it, or 409 while there are none. The answer is graded with the given
    // option against the second one being right.
    public static async Task AnswerQuizAsync(IPage page, bool ready = true, string explanation = "Die Lichtreaktion läuft an den Membranen der Thylakoide ab.")
    {
        var attempt = AttemptId;
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
                explanation,
                responseTimeMs = 1000,
                answeredAt = DateTime.UtcNow,
            });
        });
    }

    // Planning's learning goals: none yet, and a new one comes back as it was sent. What was sent lands in the list.
    public static Task AnswerGoalsAsync(IPage page, List<string> created) =>
        page.RouteAsync("**/api/planning/learning-goals", route =>
        {
            if (route.Request.Method != "POST")
            {
                return FulfillAsync(route, Array.Empty<object>());
            }

            created.Add(route.Request.PostData ?? "");
            using var sent = JsonDocument.Parse(route.Request.PostData ?? "{}");
            var goal = sent.RootElement;
            return FulfillAsync(route, new
            {
                id = Guid.NewGuid(),
                kind = goal.GetProperty("kind").GetString(),
                name = goal.GetProperty("name").GetString(),
                examType = goal.GetProperty("examType").GetString(),
                examDate = goal.GetProperty("examDate").GetString(),
                level = goal.GetProperty("level").GetString(),
                outputLanguage = goal.GetProperty("outputLanguage").GetString(),
                goal = goal.GetProperty("goal").GetString(),
                createdAt = DateTime.UnixEpoch,
            }, 201);
        });

    // The overview's list: an exam goal the plan follows, with a name that is one long word, and a free one next to it.
    public static readonly object[] Goals =
    [
        new
        {
            id = Guid.NewGuid(), kind = "ExamPreparation", name = $"Biologie-Klausur {LongWord}", examType = "Klausur",
            examDate = "2027-03-01", level = "UpperSecondary", outputLanguage = "de", goal = (string?)null,
            createdAt = DateTime.UnixEpoch, inPlan = true,
        },
        new
        {
            id = Guid.NewGuid(), kind = "FreeLearning", name = "C# lernen", examType = (string?)null, examDate = (string?)null,
            level = (string?)null, outputLanguage = "en", goal = (string?)null, createdAt = DateTime.UnixEpoch, inPlan = false,
        },
    ];

    public static Task AnswerGoalListAsync(IPage page, object[] goals) =>
        page.RouteAsync("**/api/planning/learning-goals", route => FulfillAsync(route, goals));

    private static Task FulfillAsync(IRoute route, object body, int status = 200) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = status,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        });
}