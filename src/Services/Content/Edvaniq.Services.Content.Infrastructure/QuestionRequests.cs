using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Edvaniq.Services.Content.Infrastructure;

// Asks the AI for the questions of one topic, for a new pool or new material (QuestionPools) or a top-up
// (QuestionTopUps).
internal static class QuestionRequests
{
    // The checked questions as items, or null if none held up, then with the failure of the provider if that was why.
    // An answer without a valid question gets asked again, up to StructuredOutput.DefaultAttempts, since a real model
    // may just have had a bad moment. A provider that fails or takes too long ends at once. Every attempt goes into the
    // cost log, under the purpose and the topic. System is the variant of QuestionGeneration.System the caller needs.
    // The questions are checked before they go anywhere (#298): flagged ones come back as null with Flagged, a check
    // that fails as null with its failure, so nothing goes out unchecked. A prompt that asks for cards too (#340) gets
    // them in Cards, checked the same way. An answer with questions but no card is asked again once, then its questions
    // are taken without cards: the questions matter more.
    public static async Task<(List<GeneratedItem>? Items, List<GeneratedItem> Cards, Exception? Failure, int Attempts, bool Flagged)> AskAsync(
        Document document,
        string system,
        IReadOnlyList<Guid> sectionIds,
        IReadOnlyDictionary<Guid, string> texts,
        string purpose,
        Guid topicId,
        IChatClient ai,
        TestDoubleAi testDouble,
        AiAnswers answers,
        IModeration moderation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var chat = ContentAi.For(document, ai, testDouble);
        var withCards = QuestionGeneration.AsksForCards(system);
        Drafts? withoutCards = null;
        var answer = await answers.AskAsync(
            document,
            purpose,
            QuestionGeneration.PromptVersion,
            AiReference.Topic(topicId),
            chat,
            [
                new ChatMessage(ChatRole.System, system),
                new ChatMessage(ChatRole.User, QuestionGeneration.User(sectionIds.Select(id => texts[id]))),
            ],
            Read,
            new StructuredRequest(timeout, withCards ? ContentAi.QuestionsWithCardsFormat : ContentAi.QuestionsFormat),
            cancellationToken);

        // The questions of an answer without cards beat no questions at all, even if the next attempt failed.
        var drafts = answer.Value ?? withoutCards;
        if (drafts is null)
        {
            return (null, [], answer.Failure, answer.Attempts, false);
        }

        var verdict = await moderation.CheckAsync(
            document,
            chat,
            [
                .. drafts.Questions.Select(draft => string.Join('\n', [draft.Text, .. draft.Options, draft.Explanation])),
                .. drafts.Cards.Select(card => string.Join('\n', card.Front, card.Back)),
            ],
            AiReference.Topic(topicId),
            cancellationToken);
        if (verdict.Failure is not null || verdict.Flagged)
        {
            return (null, [], verdict.Failure, answer.Attempts, verdict.Flagged);
        }

        var model = ContentAi.ModelOf(chat, answer.ModelId);

        // Shuffled here, so every pool and top-up sends its options in a random order (#1046).
        return (
            [.. drafts.Questions.Select(draft => draft.Shuffled(Random.Shared)).Select(draft => new GeneratedItem(
                Guid.NewGuid(), ItemKind.Question, document.Id, draft.SectionId, model, QuestionGeneration.PromptVersion,
                new GeneratedQuestion(
                    draft.Text, draft.Options, draft.Answer, draft.Explanation, draft.Difficulty, draft.Type,
                    draft.Type == QuestionType.MultipleChoice ? draft.Answers : null)))],
            [.. drafts.Cards.Select(card => new GeneratedItem(
                Guid.NewGuid(), ItemKind.Card, document.Id, card.SectionId, model, QuestionGeneration.PromptVersion, null,
                new GeneratedCard(card.Front, card.Back)))],
            null,
            answer.Attempts,
            false);

        Drafts? Read(string text)
        {
            if (QuestionGeneration.Read(text, sectionIds) is not { Count: > 0 } questions)
            {
                return null;
            }

            if (!withCards)
            {
                return new Drafts(questions, []);
            }

            if (QuestionGeneration.ReadCards(text, sectionIds) is { Count: > 0 } cards)
            {
                return new Drafts(questions, cards);
            }

            withoutCards ??= new Drafts(questions, []);
            return null;
        }
    }

    private sealed record Drafts(IReadOnlyList<QuestionDraft> Questions, IReadOnlyList<CardDraft> Cards);

    public static List<string> KeysOf(IEnumerable<GeneratedItem> items) =>
        [.. items.Select(item => QuestionGeneration.KeyOf(item.Question!.Text))];

    // Every key the topic has: those of the pool and those of material added later.
    public static async Task<HashSet<string>> KeysOfAsync(ContentDbContext db, QuestionPool pool, CancellationToken cancellationToken)
    {
        var added = await db.PoolMaterials.IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
            .Where(material => material.TopicId == pool.TopicId && material.QuestionKeys != null)
            .Select(material => material.QuestionKeys!)
            .ToListAsync(cancellationToken);
        return [.. (pool.QuestionKeys ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries), .. added.SelectMany(keys => keys.Split(' '))];
    }

    // The questions that ask something the topic doesn't have yet, each only once, at most take of them.
    public static List<GeneratedItem> Fresh(IEnumerable<GeneratedItem> items, IReadOnlySet<string> known, int take) =>
        [.. items
            .DistinctBy(item => QuestionGeneration.KeyOf(item.Question!.Text))
            .Where(item => !known.Contains(QuestionGeneration.KeyOf(item.Question!.Text)))
            .Take(take)];

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