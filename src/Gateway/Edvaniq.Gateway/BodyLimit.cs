namespace Edvaniq.Gateway;

// The gateway takes no larger body than the upload route of the web host and Content (Kestrel:Limits:MaxRequestBodySize,
// docs/architecture.md, "Upload-Grenze"). One that says so in Content-Length gets a 413 problem before a byte of it is
// read or forwarded, instead of the bare 400 YARP would answer once Kestrel cuts it off. One without a length Kestrel
// cuts off at the limit.
internal static class BodyLimit
{
    public const string Key = "Kestrel:Limits:MaxRequestBodySize";

    public static IApplicationBuilder UseBodyLimit(this WebApplication app)
    {
        var limit = app.Configuration.GetValue<long?>(Key)
            ?? throw new InvalidOperationException($"{Key} is not set, the gateway would take bodies the services refuse.");

        return app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength > limit)
            {
                await Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, detail: "The body is larger than allowed.")
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });
    }
}