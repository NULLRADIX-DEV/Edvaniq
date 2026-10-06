using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace Edvaniq.Gateway;

// When the service behind a route is down or too slow, YARP answers with a bare 502, 503 or 504 without a body.
// This keeps the status and adds a problem the caller can read.
internal static class ForwarderProblems
{
    public static void UseForwarderProblems(this IReverseProxyApplicationBuilder proxy) =>
        proxy.Use(async (context, next) =>
        {
            await next();

            // Once the response has started, part of the answer of the service is already on its way. If the caller
            // gave up, nobody is left to read a problem.
            var error = context.GetForwarderErrorFeature();
            if (error is null || context.Response.HasStarted || context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            // Only failures on the side of the service name it. A body the client broke off or sent too slowly
            // (400, 408) gets the plain title of its status.
            var service = context.GetReverseProxyFeature().Route.Config.ClusterId;
            var detail = error.Error switch
            {
                ForwarderError.Request
                    or ForwarderError.RequestBodyDestination
                    or ForwarderError.ResponseHeaders
                    or ForwarderError.NoAvailableDestinations => $"The service '{service}' is not reachable right now.",
                ForwarderError.RequestTimedOut => $"The service '{service}' did not answer in time.",
                _ => null,
            };

            await Results.Problem(statusCode: context.Response.StatusCode, detail: detail).ExecuteAsync(context);
        });
}