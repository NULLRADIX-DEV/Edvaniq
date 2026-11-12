using Edvaniq.Services.Content.ReferenceRun;

using Microsoft.Extensions.Configuration;

// The provider and its key come from the environment only (Ai__Provider, Ai__…), never from a file in the repo.
return await ReferenceRun.RunAsync(args, configuration => configuration.AddEnvironmentVariables(), Console.Out, CancellationToken.None);