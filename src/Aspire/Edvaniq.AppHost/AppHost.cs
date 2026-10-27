using Edvaniq.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// Database server
var mysql = builder.AddMySql("mysql")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPhpMyAdmin();

// Message broker for the integration events: NATS with JetStream. Only the AppHost starts it so far. A service gets its
// address as ConnectionStrings:nats (WithMessaging), and without it runs without a broker. The image tag is the one of
// the tests (NatsImageTests).
var nats = builder.AddNats("nats")
    .WithImageTag("2.15")
    .WithJetStream()
    .WithDataVolume();

// Databases (one per service, each with its own user)
var identityDb = mysql.AddServiceDatabase("identity");
var planningDb = mysql.AddServiceDatabase("planning");
var contentDb = mysql.AddServiceDatabase("content");
var knowledgeDb = mysql.AddServiceDatabase("knowledge");
var assessmentDb = mysql.AddServiceDatabase("assessment");
var flashcardsDb = mysql.AddServiceDatabase("flashcards");
var learningEngineDb = mysql.AddServiceDatabase("learningengine");
var tutorDb = mysql.AddServiceDatabase("tutor");
var gamificationDb = mysql.AddServiceDatabase("gamification");
var analyticsDb = mysql.AddServiceDatabase("analytics");
var notificationsDb = mysql.AddServiceDatabase("notifications");

// Migrations (one step per service from the template, its API waits until it has finished)
var planningMigrate = builder.AddMigration<Projects.Edvaniq_Services_Planning_Api>("planning-migrate", planningDb);
var contentMigrate = builder.AddMigration<Projects.Edvaniq_Services_Content_Api>("content-migrate", contentDb);

// Token issuer: Identity signs the tokens, every service checks them with the public key it gets from Identity
var signingKey = builder.AddSigningKey("identity-signing-key");
var webClientSecret = builder.AddClientSecret("web-client-secret");
var identityApi = builder.AddProject<Projects.Edvaniq_Services_Identity_Api>("identity-api")
    .WithDatabase(identityDb)
    .WithTokenIssuer(signingKey, webClientSecret);

// Services
var planningApi = AddService<Projects.Edvaniq_Services_Planning_Api>("planning-api", planningDb)
    .WaitForCompletion(planningMigrate)
    .WithMessaging(nats);
var contentApi = AddService<Projects.Edvaniq_Services_Content_Api>("content-api", contentDb)
    .WaitForCompletion(contentMigrate)
    .WithMessaging(nats);
var knowledgeApi = AddService<Projects.Edvaniq_Services_Knowledge_Api>("knowledge-api", knowledgeDb);
var assessmentApi = AddService<Projects.Edvaniq_Services_Assessment_Api>("assessment-api", assessmentDb);
var flashcardsApi = AddService<Projects.Edvaniq_Services_Flashcards_Api>("flashcards-api", flashcardsDb);
var learningEngineApi = AddService<Projects.Edvaniq_Services_LearningEngine_Api>("learningengine-api", learningEngineDb);
var tutorApi = AddService<Projects.Edvaniq_Services_Tutor_Api>("tutor-api", tutorDb);
var gamificationApi = AddService<Projects.Edvaniq_Services_Gamification_Api>("gamification-api", gamificationDb);
var analyticsApi = AddService<Projects.Edvaniq_Services_Analytics_Api>("analytics-api", analyticsDb);
var notificationsApi = AddService<Projects.Edvaniq_Services_Notifications_Api>("notifications-api", notificationsDb);

// Workers (share the database of their service)
AddService<Projects.Edvaniq_Services_Content_Worker>("content-worker", contentDb)
    .WaitForCompletion(contentMigrate)
    .WithMessaging(nats);
AddService<Projects.Edvaniq_Services_Notifications_Worker>("notifications-worker", notificationsDb);

// Gateway
var gateway = builder.AddProject<Projects.Edvaniq_Gateway>("gateway")
    .WithExternalHttpEndpoints()
    .WithReference(identityApi)
    .WithReference(planningApi)
    .WithReference(contentApi)
    .WithReference(knowledgeApi)
    .WithReference(assessmentApi)
    .WithReference(flashcardsApi)
    .WithReference(learningEngineApi)
    .WithReference(tutorApi)
    .WithReference(gamificationApi)
    .WithReference(analyticsApi)
    .WithReference(notificationsApi);

// Frontend
builder.AddProject<Projects.Edvaniq_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(gateway)
    .WithEnvironment("BackendToken__ClientSecret", webClientSecret);

builder.Build().Run();

IResourceBuilder<ProjectResource> AddService<TProject>(string name, ServiceDatabase database)
    where TProject : IProjectMetadata, new() =>
    builder.AddProject<TProject>(name)
        .WithDatabase(database)
        .WithTokenValidation(identityApi);