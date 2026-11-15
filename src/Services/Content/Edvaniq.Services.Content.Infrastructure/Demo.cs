using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Demo(ContentDbContext db, ICurrentUser currentUser, TimeProvider time) : IDemo
{
    // The material is ours, the confirmation says so instead of a version of the user's wording.
    public const string Confirmation = "demo";

    public async Task<IReadOnlyList<Document>> DocumentsAsync(CancellationToken cancellationToken) =>
        await db.Documents.Where(document => document.Origin == DocumentOrigin.Demo)
            .OrderBy(document => document.CreatedAt)
            .ToListAsync(cancellationToken);

    // A microsecond apart, so the documents keep the order of the material.
    public async Task<IReadOnlyList<Document>> CreateAsync(LearningGoalInfo learningGoal, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var documents = DemoMaterial.Documents.Select((material, index) =>
        {
            var createdAt = now.AddTicks(index * 10);
            var document = Document.Paste(
                currentUser.Id, learningGoal.Id, material.Title, material.Text, createdAt, origin: DocumentOrigin.Demo);
            document.ConfirmMaterial(Confirmation, createdAt);
            return document;
        }).ToList();
        db.Documents.AddRange(documents);
        await db.SaveChangesAsync(cancellationToken);
        return documents;
    }
}