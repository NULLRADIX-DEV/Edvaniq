using Yarp.ReverseProxy.Transforms;

namespace Edvaniq.Gateway;

// A service that fails without a problem, with an error page or an empty body, would show the caller how it is built.
// Its answer is replaced by a problem of the gateway with the same status, headers included. A problem from the
// service, as every service from the template writes it, gets through as it is.
internal static class ServiceErrorProblems
{
    public static IReverseProxyBuilder AddServiceErrorProblems(this IReverseProxyBuilder proxy) =>
        proxy.AddTransforms(transforms =>
        {
            var service = transforms.Route.ClusterId;
            transforms.AddResponseTransform(async context =>
            {
                var answer = context.ProxyResponse;
                var response = context.HttpContext.Response;
                if (answer is null || (int)answer.StatusCode < 500 || IsProblem(answer) || response.HasStarted)
                {
                    return;
                }

                context.SuppressResponseBody = true;
                response.Headers.Clear();
                await Results.Problem(statusCode: (int)answer.StatusCode, detail: $"The service '{service}' failed.")
                    .ExecuteAsync(context.HttpContext);
            });
        });

    private static bool IsProblem(HttpResponseMessage answer) =>
        answer.Content.Headers.ContentType?.MediaType == "application/problem+json";
}