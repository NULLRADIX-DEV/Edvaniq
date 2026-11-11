using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Content.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddWorkerInfrastructure();
builder.AddContentAi();

// Knowledge says which ids the topics of an analysis got, asked with the worker's own token.
builder.Services.AddOptions<InternalTokenOptions>()
    .BindConfiguration(InternalTokenOptions.Section)
    .Validate(options => options.ClientSecret.Length > 0, "InternalToken:ClientSecret is missing.")
    .ValidateOnStart();
builder.Services.AddSingleton<InternalToken>();
builder.Services.AddTransient<InternalTokenHandler>();
builder.Services.AddHttpClient(InternalToken.ClientName, client => client.BaseAddress = InternalToken.IdentityAddress);
builder.Services.AddHttpClient<IKnowledgeTopics, KnowledgeTopics>(client => client.BaseAddress = KnowledgeTopics.Address)
    .AddHttpMessageHandler<InternalTokenHandler>();

var host = builder.Build();
host.Run();