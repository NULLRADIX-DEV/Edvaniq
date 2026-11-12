using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Infrastructure;

namespace Edvaniq.Services.Content.Api;

// dotnet Edvaniq.Services.Content.Api.dll document delete <id>, run by the operator on the server.
// Deletes the document exactly as its owner would with DELETE /documents/{id}: file, sections and pools go, DocumentDeleted
// goes into the outbox in the same save and the running content-api sends it. There is no HTTP way to this, the command
// acts as the owner instead of skipping the owner filter. Prints only the document id, never the owner.
internal static partial class DocumentCommand
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Invalid = 2;

    public static bool Matches(string[] args) => args is ["document", ..];

    // Program calls this only when the command runs, so the web app never gets an operator user.
    public static void Register(IServiceCollection services)
    {
        services.AddScoped<OperatorUser>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<OperatorUser>());
        services.AddDocumentOwners();
    }

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, TextWriter error)
    {
        if (args is not ["document", "delete", var text] || !Guid.TryParse(text, out var id))
        {
            await error.WriteLineAsync("Usage: document delete <id>, the id of the document as a GUID.");
            return Invalid;
        }

        try
        {
            using var scope = services.CreateScope();
            var owner = await scope.ServiceProvider.GetRequiredService<IDocumentOwners>().OwnerOfAsync(id, CancellationToken.None);
            var deleted = owner is not null && await DeleteAsAsync(scope.ServiceProvider, owner, id);
            await output.WriteLineAsync($"{(deleted ? "deleted" : "no document")} {id:D}");
            return Ok;
        }
        catch (Exception exception)
        {
            LogFailed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DocumentCommand)), exception);
            await error.WriteLineAsync("Deleting the document failed, see the log above.");
            return Failed;
        }
    }

    private static Task<bool> DeleteAsAsync(IServiceProvider scope, string owner, Guid id)
    {
        scope.GetRequiredService<OperatorUser>().ActAs(owner);
        return scope.GetRequiredService<IDocuments>().DeleteAsync(id, CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Deleting the document failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

// The user the command acts as. Until it knows the owner, every query under the owner filter fails.
internal sealed class OperatorUser : ICurrentUser
{
    private string? id;

    public string Id => id ?? throw new InvalidOperationException("The command has not looked up the owner yet.");

    public void ActAs(string owner) => id = owner;
}