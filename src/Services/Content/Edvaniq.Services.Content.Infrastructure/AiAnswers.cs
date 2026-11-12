using System.Security.Cryptography;
using System.Text;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// Every AI call of a document goes through here (#306). A request the same user had answered before is answered from
// AiAnswer, no provider is asked and nothing is paid, and the cost log says so. Otherwise it asks as ever, logs every
// attempt (AiUsageLog) and keeps a usable answer. Both in saves of their own, like the cost log: a database that is
// away for a moment costs the reuse, not the analysis.
public sealed class AiAnswers(IServiceScopeFactory scopes, AiUsageLog usage, TimeProvider time, ILogger<AiAnswers> logger)
{
    public async Task<StructuredAnswer<T>> AskAsync<T>(
        Document document,
        string purpose,
        string promptVersion,
        IChatClient chat,
        IReadOnlyList<ChatMessage> messages,
        Func<string, T?> read,
        StructuredRequest request,
        CancellationToken cancellationToken)
        where T : class
    {
        var model = ContentAi.ModelOf(chat, null);
        var key = KeyOf(purpose, promptVersion, model, document.UsesTestDouble, messages);
        if (await StoredAsync(document, key, cancellationToken) is { } stored && read(stored.Answer) is { } reused)
        {
            usage.Reused(document, purpose, promptVersion, stored.Model);
            return new StructuredAnswer<T>(reused, 0, new UsageDetails(), stored.Model, null, []);
        }

        string? usable = null;
        var answer = await chat.AskAsync(
            messages,
            text => read(text) is { } value ? Keep(value, text) : null,
            request,
            time,
            cancellationToken);
        await usage.RecordAsync(document, purpose, promptVersion, answer.Tries, model, cancellationToken);
        if (answer.Value is not null && usable is not null)
        {
            await StoreAsync(AiAnswer.Of(
                document, key, purpose, promptVersion, ContentAi.ModelOf(chat, answer.ModelId), usable, time.GetUtcNow().UtcDateTime), cancellationToken);
        }

        return answer;

        T Keep(T value, string text)
        {
            usable = text;
            return value;
        }
    }

    // Everything the answer depends on: what is asked and how (purpose, prompt version, the messages with level,
    // language and material), and who answers (the configured model, or the test double of a test account).
    internal static string KeyOf(string purpose, string promptVersion, string model, bool testDouble, IReadOnlyList<ChatMessage> messages)
    {
        var text = new StringBuilder()
            .Append(purpose).Append('\0').Append(promptVersion).Append('\0').Append(model).Append('\0').Append(testDouble ? '1' : '0');
        foreach (var message in messages)
        {
            text.Append('\0').Append(message.Role.Value).Append('\0').Append(message.Text.ReplaceLineEndings("\n"));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private async Task<AiAnswer?> StoredAsync(Document document, string key, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().AiAnswers
                .IgnoreQueryFilters([ServiceDbContext.OwnerFilter])
                .AsNoTracking()
                .FirstOrDefaultAsync(answer => answer.OwnerId == document.OwnerId && answer.Key == key, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            ContentLog.AiAnswerNotRead(logger, exception, document.Id);
            return null;
        }
    }

    private async Task StoreAsync(AiAnswer answer, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
            db.AiAnswers.Add(answer);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Another worker stored it first, or the document is gone: the answer was used all the same.
            ContentLog.AiAnswerNotStored(logger, exception, answer.DocumentId);
        }
    }
}