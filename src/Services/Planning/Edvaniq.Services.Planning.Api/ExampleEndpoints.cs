using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;

namespace Edvaniq.Services.Planning.Api;

// Endpoints of the example (ExampleItem). Replace them together with it. The owner never appears in a request or a
// response: it comes from the token, and the owner filter applies it.
internal static class ExampleEndpoints
{
    public static IEndpointRouteBuilder MapExampleEndpoints(this IEndpointRouteBuilder app)
    {
        var examples = app.MapGroup("/examples");

        examples.MapPost("/", async (NewExampleItem request, IExampleItems items, CancellationToken cancellationToken) =>
        {
            if (!ExampleItem.IsValidText(request.Text))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Text)] = [$"Text is required and has at most {ExampleItem.TextMaxLength} characters."],
                });
            }

            var item = await items.AddAsync(request.Text, cancellationToken);
            return Results.Created($"/examples/{item.Id}", ExampleItemResponse.From(item));
        });

        examples.MapGet("/", async (IExampleItems items, CancellationToken cancellationToken) =>
            (await items.ListAsync(cancellationToken)).Select(ExampleItemResponse.From));

        // Someone else's item and an item that does not exist look the same: 404.
        examples.MapGet("/{id:guid}", async (Guid id, IExampleItems items, CancellationToken cancellationToken) =>
            await items.FindAsync(id, cancellationToken) is { } item
                ? Results.Ok(ExampleItemResponse.From(item))
                : Results.NotFound());

        return app;
    }
}

internal sealed record NewExampleItem(string Text);

internal sealed record ExampleItemResponse(Guid Id, string Text)
{
    public static ExampleItemResponse From(ExampleItem item) => new(item.Id, item.Text);
}
