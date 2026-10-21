using System.Diagnostics;

using Microsoft.AspNetCore.Http;

namespace Edvaniq.ServiceDefaults;

public static class TraceIds
{
    // The id a problem names as traceId and every log line of the request carries as TraceId: the 32 hex digits of the
    // W3C trace. Without an activity the request's own identifier stands in.
    public static string GetTraceId(this HttpContext context) =>
        Activity.Current?.TraceId.ToHexString() ?? context.TraceIdentifier;
}