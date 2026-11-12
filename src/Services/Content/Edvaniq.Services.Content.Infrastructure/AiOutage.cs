using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// What a worker does when the provider failed on a document: a failure that may pass postpones it (the worker goes on
// with the others and asks again later), anything else fails it at once, since asking again would fail the same way.
internal static class AiOutage
{
    public static void Handle(
        Document document, string task, Exception failure, DateTime now, IReadOnlyList<TimeSpan> delays, ILogger logger)
    {
        // On purpose, so no waiting for it to pass: the document fails, and a retry works once the AI runs again.
        if (failure is AiPausedException)
        {
            ContentLog.AiPaused(logger, task, document.Id);
            document.Fail(Document.AiPaused);
            return;
        }

        if (!AiFailure.IsTransient(failure))
        {
            // Most likely the key or the settings, so every document fails like this one. The status only, no body.
            ContentLog.AiRefused(logger, task, document.Id, failure is HttpRequestException { StatusCode: { } status } ? (int)status : null, failure.GetType().Name);
            document.Fail(Document.AiUnavailable);
            return;
        }

        if (document.Postpone(now, delays, AiFailure.RetryAfter(failure, now)))
        {
            ContentLog.AiUnavailable(logger, task, document.Id, failure.GetType().Name, document.AiOutages, document.NextAttemptAt);
        }
        else
        {
            ContentLog.AiStaysUnavailable(logger, task, document.Id, delays.Count + 1);
        }
    }
}