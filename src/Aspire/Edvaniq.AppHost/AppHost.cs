using Edvaniq.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// Database server
var mysql = builder.AddMySql("mysql")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPhpMyAdmin();

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

// Services
var identityApi = AddService<Projects.Edvaniq_Services_Identity_Api>("identity-api", identityDb);
var planningApi = AddService<Projects.Edvaniq_Services_Planning_Api>("planning-api", planningDb)
    .WaitForCompletion(planningMigrate);
var contentApi = AddService<Projects.Edvaniq_Services_Content_Api>("content-api", contentDb);
var knowledgeApi = AddService<Projects.Edvaniq_Services_Knowledge_Api>("knowledge-api", knowledgeDb);
var assessmentApi = AddService<Projects.Edvaniq_Services_Assessment_Api>("assessment-api", assessmentDb);
var flashcardsApi = AddService<Projects.Edvaniq_Services_Flashcards_Api>("flashcards-api", flashcardsDb);
var learningEngineApi = AddService<Projects.Edvaniq_Services_LearningEngine_Api>("learningengine-api", learningEngineDb);
var tutorApi = AddService<Projects.Edvaniq_Services_Tutor_Api>("tutor-api", tutorDb);
var gamificationApi = AddService<Projects.Edvaniq_Services_Gamification_Api>("gamification-api", gamificationDb);
var analyticsApi = AddService<Projects.Edvaniq_Services_Analytics_Api>("analytics-api", analyticsDb);
var notificationsApi = AddService<Projects.Edvaniq_Services_Notifications_Api>("notifications-api", notificationsDb);

// Workers (share the database of their service)
AddService<Projects.Edvaniq_Services_Content_Worker>("content-worker", contentDb);
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
    .WithReference(gateway);

builder.Build().Run();

IResourceBuilder<ProjectResource> AddService<TProject>(string name, ServiceDatabase database)
    where TProject : IProjectMetadata, new() =>
    builder.AddProject<TProject>(name)
        .WithDatabase(database);
