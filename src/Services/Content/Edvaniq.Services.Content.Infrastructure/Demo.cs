using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// No query here mentions the owner: the filter of ServiceDbContext adds it to every one of them.
internal sealed class Demo(ContentDbContext db, IDocuments documents, ICurrentUser currentUser, TimeProvider time) : IDemo
{
    // The material is ours, the confirmation says so instead of a version of the user's wording.
    public const string Confirmation = "demo";

    public async Task<DemoState?> FindAsync(CancellationToken cancellationToken) =>
        await db.DemoGoals.AsNoTracking().FirstOrDefaultAsync(cancellationToken) is { } goal
            ? new DemoState(goal.LearningGoalId, await DocumentsAsync(cancellationToken))
            : null;

    // The demo row and the documents in one save: of two requests at the same time, the second fails on the row's key
    // and gets the demo of the first. The documents lie a tick apart, so they keep the order of the material.
    public async Task<(bool Created, DemoState Demo)> CreateAsync(LearningGoalInfo learningGoal, CancellationToken cancellationToken)
    {
        if (await FindAsync(cancellationToken) is { } existing)
        {
            return (false, existing);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var pasted = DemoMaterial.Documents.Select((material, index) =>
        {
            var createdAt = now.AddTicks(index * 10);
            var document = Document.Paste(
                currentUser.Id, learningGoal.Id, material.Title, material.Text, createdAt, origin: DocumentOrigin.Demo);
            document.ConfirmMaterial(Confirmation, createdAt);
            return document;
        }).ToList();
        db.DemoGoals.Add(DemoGoal.Of(currentUser.Id, learningGoal.Id, now));
        db.Documents.AddRange(pasted);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await FindAsync(cancellationToken) is { } other)
            {
                return (false, other);
            }

            throw;
        }

        return (true, new DemoState(learningGoal.Id, pasted));
    }

    // Each document goes the way of DELETE /documents/{id}, with DocumentDeleted to the other services. The row goes
    // last, so a delete that breaks off can be sent again.
    public async Task<bool> DeleteAsync(CancellationToken cancellationToken)
    {
        var demoDocuments = await DocumentsAsync(cancellationToken);
        foreach (var document in demoDocuments)
        {
            await documents.DeleteAsync(document.Id, cancellationToken);
        }

        return await db.DemoGoals.ExecuteDeleteAsync(cancellationToken) > 0 || demoDocuments.Count > 0;
    }

    private async Task<IReadOnlyList<Document>> DocumentsAsync(CancellationToken cancellationToken) =>
        await db.Documents.AsNoTracking()
            .Where(document => document.Origin == DocumentOrigin.Demo)
            .OrderBy(document => document.CreatedAt)
            .ToListAsync(cancellationToken);
}