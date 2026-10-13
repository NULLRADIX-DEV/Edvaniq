using Microsoft.AspNetCore.Http.Metadata;

namespace Edvaniq.Web;

// /api and /session skip the HTML not-found page (SkipStatusCodePagesAttribute). Their own errors come without a body:
// 401 without a session, 502 without a token, 502 or 504 when the gateway is not reachable. This turns them into
// problems that name the trace (ServiceDefaults), so a client can pass the id on. Problems from the gateway or a
// service already have a body and pass through unchanged, they name the same trace.
public static class ApiProblems
{
    public static IApplicationBuilder UseApiProblems(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next();

            var response = context.Response;
            if (context.GetEndpoint()?.Metadata.GetMetadata<ISkipStatusCodePagesMetadata>() is null
                || response.StatusCode < StatusCodes.Status400BadRequest
                || response.HasStarted
                || response.ContentLength is not null
                || !string.IsNullOrEmpty(response.ContentType)
                || context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            // A client that takes no JSON (Accept) keeps the bare status. WriteAsync would throw and turn it into a 500.
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = { Status = response.StatusCode },
            });
        });
}