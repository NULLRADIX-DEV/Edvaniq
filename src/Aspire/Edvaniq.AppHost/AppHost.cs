var builder = DistributedApplication.CreateBuilder(args);

// Services
var identityApi = builder.AddProject<Projects.Edvaniq_Services_Identity_Api>("identity-api");
var planningApi = builder.AddProject<Projects.Edvaniq_Services_Planning_Api>("planning-api");
var contentApi = builder.AddProject<Projects.Edvaniq_Services_Content_Api>("content-api");
var knowledgeApi = builder.AddProject<Projects.Edvaniq_Services_Knowledge_Api>("knowledge-api");
var assessmentApi = builder.AddProject<Projects.Edvaniq_Services_Assessment_Api>("assessment-api");
var flashcardsApi = builder.AddProject<Projects.Edvaniq_Services_Flashcards_Api>("flashcards-api");
var learningEngineApi = builder.AddProject<Projects.Edvaniq_Services_LearningEngine_Api>("learningengine-api");
var tutorApi = builder.AddProject<Projects.Edvaniq_Services_Tutor_Api>("tutor-api");
var gamificationApi = builder.AddProject<Projects.Edvaniq_Services_Gamification_Api>("gamification-api");
var analyticsApi = builder.AddProject<Projects.Edvaniq_Services_Analytics_Api>("analytics-api");
var notificationsApi = builder.AddProject<Projects.Edvaniq_Services_Notifications_Api>("notifications-api");

// Workers
builder.AddProject<Projects.Edvaniq_Services_Content_Worker>("content-worker");
builder.AddProject<Projects.Edvaniq_Services_Notifications_Worker>("notifications-worker");

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
