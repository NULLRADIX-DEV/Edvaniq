using Microsoft.AspNetCore.Builder;

namespace Edvaniq.BuildingBlocks.Web;

public static class ProblemExtensions
{
    // Every error answers as a problem that names the trace of the request (traceId, set by ServiceDefaults, which also
    // registers the problem details this needs). An unhandled exception becomes a bare 500 without message or stack
    // trace, a 401 from the token check or a 404 without a body get the same form. Called first, so it wraps health and
    // the token check too.
    public static WebApplication UseProblems(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}