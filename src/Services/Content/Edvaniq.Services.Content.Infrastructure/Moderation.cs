using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure;

// The check of material and AI output before anything goes on (#298). Exchangeable: another service behind it needs
// no change in the worker. chat is the AI the document uses, a check through the AI goes the same way as its other
// calls (AiGate, cost log), one that doesn't ignores it.
public interface IModeration
{
    Task<ModerationVerdict> CheckAsync(
        Document document, IChatClient chat, IReadOnlyList<string> texts, AiReference reference, CancellationToken cancellationToken);
}

// Safe, flagged with the codes that stop it, or not checked because the check failed. Not checked never passes.
public sealed record ModerationVerdict(IReadOnlyList<string> Codes, Exception? Failure)
{
    public static readonly ModerationVerdict Safe = new([], null);

    public bool Flagged => Codes.Count > 0;
}

// The check gave no answer it could read, twice. Not transient: the document fails like with a provider that refuses.
public sealed class ModerationUnreadableException() : Exception("The moderation gave no readable answer.");

public sealed class ModerationOptions
{
    public const string Section = "Ai:Moderation";

    // The model that classifies, on the same provider as the rest. Only configuration, required for a real provider.
    public string? Model { get; set; }

    // The routing of that model, a JSON object like Ai:OpenAiCompatible:ExtraBody, which it replaces for the check.
    public string? ExtraBody { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);

    public int PackageCharacters { get; set; } = ModerationCheck.DefaultPackageCharacters;
}

// The local list first, then the classifier package by package, through AiAnswers: the same text of the same user
// is not checked and paid twice. The log names codes, never text.
internal sealed class AiModeration : IModeration
{
    private readonly AiAnswers answers;

    private readonly ModerationOptions options;

    private readonly JsonObject? extraBody;

    private readonly ILogger<AiModeration> logger;

    public AiModeration(AiAnswers answers, IOptions<ModerationOptions> options, ILogger<AiModeration> logger)
    {
        this.answers = answers;
        this.options = options.Value;
        this.logger = logger;
        var body = AiRequest.ParseExtraBody(this.options.ExtraBody, $"{ModerationOptions.Section}:ExtraBody");
        extraBody = body.Count == 0 ? null : body;
    }

    public async Task<ModerationVerdict> CheckAsync(
        Document document, IChatClient chat, IReadOnlyList<string> texts, AiReference reference, CancellationToken cancellationToken)
    {
        if (texts.Any(ModerationCheck.HitsLocalList))
        {
            ContentLog.ModerationFlagged(logger, document.Id, ModerationCheck.Local);
            return new ModerationVerdict([ModerationCheck.Local], null);
        }

        var request = new StructuredRequest(options.Timeout, ModelId: options.Model, ExtraBody: extraBody, Task: ModerationCheck.Task);
        foreach (var package in ModerationCheck.Packages(texts, options.PackageCharacters))
        {
            var answer = await answers.AskAsync(
                document,
                AiAttempt.Moderation,
                ModerationCheck.PromptVersion,
                reference,
                chat,
                [new ChatMessage(ChatRole.User, package)],
                ModerationCheck.Read,
                request,
                cancellationToken);
            if (answer.Failure is { } failure)
            {
                return new ModerationVerdict([], failure);
            }

            if (answer.Value is not { } codes)
            {
                ContentLog.ModerationUnreadable(logger, document.Id, answer.Attempts);
                return new ModerationVerdict([], new ModerationUnreadableException());
            }

            if (codes.Count > 0)
            {
                ContentLog.ModerationFlagged(logger, document.Id, string.Join(',', codes));
                return new ModerationVerdict(codes, null);
            }
        }

        return ModerationVerdict.Safe;
    }
}