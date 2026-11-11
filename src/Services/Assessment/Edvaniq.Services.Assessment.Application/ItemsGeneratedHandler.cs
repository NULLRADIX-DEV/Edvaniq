using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Content.Contracts;

using Microsoft.Extensions.Logging;

using GeneratedType = Edvaniq.Services.Content.Contracts.QuestionType;
using QuestionType = Edvaniq.Services.Assessment.Domain.QuestionType;

namespace Edvaniq.Services.Assessment.Application;

// Takes over the questions of one topic (docs/spec/events.md), only for a topic of the user that TopicsChanged made
// known. Content sends the questions only once Knowledge has the topic, and Knowledge stores TopicsChanged with it, so a
// topic not known yet is on its way: the message comes again later (MessageNotReadyException). A message from before
// the topicId came has no topic to go to and is dropped. The inbox drops a redelivered message, this skips items stored
// already, so the same items under a new message id add nothing. If two arrive at once, the key makes the later save
// fail, the broker delivers it again and this finds the questions. Cards belong to Flashcards (M4). An item that breaks
// the contract is left out with a log line, so the rest of the topic still arrives. A newer PromptVersion is only where
// the question came from, it's taken as is.
public sealed partial class ItemsGeneratedHandler(
    IQuestions questions, IKnownTopics topics, TimeProvider time, ILogger<ItemsGeneratedHandler> logger)
    : IIntegrationEventHandler<ItemsGenerated>
{
    public async Task HandleAsync(ItemsGenerated integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.TopicId is not { } topicId)
        {
            LogWithoutTopic(integrationEvent.DocumentId);
            return;
        }

        var items = integrationEvent.Items
            .Where(item => item.Kind == ItemKind.Question)
            .DistinctBy(item => item.ItemId)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        if (!await topics.IsKnownAsync(integrationEvent.UserId, topicId, cancellationToken))
        {
            throw new MessageNotReadyException($"Topic {topicId} is not known yet.");
        }

        var known = await questions.KnownAsync([.. items.Select(item => item.ItemId)], cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var item in items.Where(item => !known.Contains(item.ItemId)))
        {
            if (item.Question is not { } question
                || TypeOf(question.Type) is not { } type
                || !Question.IsValid(
                    question.Text, question.Options, question.Answer, question.Explanation, question.Difficulty, item.ModelId, item.PromptVersion,
                    type, question.Answers))
            {
                LogInvalidItem(item.ItemId, integrationEvent.DocumentId);
                continue;
            }

            questions.Add(Question.Create(
                item.ItemId,
                integrationEvent.UserId,
                topicId,
                integrationEvent.DocumentId,
                integrationEvent.TopicPosition,
                item.SectionId,
                question.Text,
                question.Options,
                question.Answer,
                question.Explanation,
                question.Difficulty,
                item.ModelId,
                item.PromptVersion,
                now,
                type,
                question.Answers));
        }
    }

    // No type is single choice, as sent before types came. A type this service doesn't know can't be graded.
    private static QuestionType? TypeOf(GeneratedType? type) => type switch
    {
        null or GeneratedType.SingleChoice => QuestionType.SingleChoice,
        GeneratedType.MultipleChoice => QuestionType.MultipleChoice,
        GeneratedType.TrueFalse => QuestionType.TrueFalse,
        _ => null,
    };

    // Ids only, never the content of a question.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Item {ItemId} of document {DocumentId} is no valid question and was left out")]
    private partial void LogInvalidItem(Guid itemId, Guid documentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Questions of document {DocumentId} name no topic and were dropped")]
    private partial void LogWithoutTopic(Guid documentId);
}