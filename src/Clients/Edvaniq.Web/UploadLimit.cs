using Microsoft.AspNetCore.Http.Features;

using Yarp.ReverseProxy.Model;

namespace Edvaniq.Web;

// A route with the metadata MaxRequestBodySize takes no larger body. One that says so in Content-Length gets the 413
// problem before a byte of it is read, one without (chunked) is cut off by Kestrel at the limit. Other routes keep
// Kestrel's default.
public static class UploadLimit
{
    public const string MetadataKey = "MaxRequestBodySize";

    // Same words as FeedbackTexts.Upload.TooLarge and Content's FileCheck.TooLarge.
    public const string TooLarge = "Die Datei ist zu groß. Erlaubt sind höchstens 20 MB.";

    public static IApplicationBuilder UseUploadLimit(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var value = context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config.Metadata?.GetValueOrDefault(MetadataKey);
            if (!long.TryParse(value, out var limit))
            {
                await next();
                return;
            }

            if (context.Request.ContentLength > limit)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context,
                    ProblemDetails = { Status = StatusCodes.Status413PayloadTooLarge, Title = TooLarge },
                });
                return;
            }

            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = limit;
            }

            await next();
        });
}