using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;

using Microsoft.Extensions.AI;

namespace Edvaniq.Services.Content.Infrastructure;

// Asks the AI for the questions of one topic, for a new pool (QuestionPools) or a top-up (QuestionTopUps).
internal static class QuestionRequests
{
    // The checked questions as items, or null if none held up, then with the failure of the provider if that was why.
    // An answer without a valid question gets asked again, up to StructuredOutput.DefaultAttempts, since a real model
    // may just have had a bad moment. A provider that fails or takes too long ends at once. Every attempt goes into the
    // cost log, under the purpose and the topic. Have is the number of questions the pool has already.
    public static async Task<(List<GeneratedItem>? Items, Exception? Failure, int Attempts)> AskAsync(
        Document document,
        LearnerProfile profile,
        IReadOnlyList<Guid> sectionIds,
        IReadOnlyDictionary<Guid, string> texts,
        int have,
        string purpose,
        Guid topicId,
        IChatClient ai,
        TestDoubleAi testDouble,
        AiAnswers answers,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var chat = ContentAi.For(document, ai, testDouble);
        var answer = await answers.AskAsync(
            document,
            purpose,
            QuestionGeneration.PromptVersion,
            AiReference.Topic(topicId),
            chat,
            [
                new ChatMessage(ChatRole.System, QuestionGeneration.System(profile, have)),
                new ChatMessage(ChatRole.User, QuestionGeneration.User(sectionIds.Select(id => texts[id]))),
            ],
            text => QuestionGeneration.Read(text, sectionIds) is { Count: > 0 } drafts ? drafts : null,
            new StructuredRequest(timeout, ContentAi.QuestionsFormat),
            cancellationToken);
        if (answer.Failure is { } failure)
        {
            return (null, failure, answer.Attempts);
        }

        if (answer.Value is not { } drafts)
        {
            return (null, null, answer.Attempts);
        }

        var model = ContentAi.ModelOf(chat, answer.ModelId);

        // Shuffled here, so every pool and top-up sends its options in a random order (#1046).
        return ([.. drafts.Select(draft => draft.Shuffled(Random.Shared)).Select(draft => new GeneratedItem(
            Guid.NewGuid(), ItemKind.Question, document.Id, draft.SectionId, model, QuestionGeneration.PromptVersion,
            new GeneratedQuestion(
                draft.Text, draft.Options, draft.Answer, draft.Explanation, draft.Difficulty, draft.Type,
                draft.Type == QuestionType.MultipleChoice ? draft.Answers : null)))], null, answer.Attempts);
    }

    public static List<string> KeysOf(IEnumerable<GeneratedItem> items) =>
        [.. items.Select(item => QuestionGeneration.KeyOf(item.Question!.Text))];

    // A topic's own sections of this document. One without any, like the roof of its subtopics, gets theirs. Knowledge
    // merges topics of the same name across documents (#208), so a topic may have sections only in another document:
    // filtering before the fallback keeps it from asking the AI about nothing, it simply has no material here.
    public static List<Guid> SectionsOf(IReadOnlyList<KnowledgeTopic> topics, KnowledgeTopic topic, IReadOnlyDictionary<Guid, string> texts)
    {
        List<Guid> own = [.. topic.SectionIds.Where(texts.ContainsKey)];
        return own.Count > 0
            ? own
            : [.. topics.Where(other => other.ParentId == topic.Id).SelectMany(other => other.SectionIds).Where(texts.ContainsKey)];
    }
}