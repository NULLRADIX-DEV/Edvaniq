using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Identity.Infrastructure;

internal sealed class InviteCodes(IdentityDbContext db) : IInviteCodes
{
    public void Add(InviteCode invite) => db.InviteCodes.Add(invite);

    public async Task<IReadOnlyList<InviteCode>> ListAsync(CancellationToken cancellationToken) =>
        await db.InviteCodes.AsNoTracking().OrderBy(invite => invite.CreatedAt).ThenBy(invite => invite.Label).ToListAsync(cancellationToken);

    public Task<InviteCode?> FindByLabelAsync(string label, CancellationToken cancellationToken) =>
        db.InviteCodes.FirstOrDefaultAsync(invite => invite.Label == label, cancellationToken);

    // One UPDATE with the open state in its WHERE: MySQL locks the row, the second of two redemptions finds it redeemed
    // and changes nothing.
    public async Task<bool> TryMarkRedeemedAsync(string codeHash, DateTime redeemedAt, CancellationToken cancellationToken) =>
        await db.InviteCodes
            .Where(invite => invite.CodeHash == codeHash && invite.RedeemedAt == null && invite.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(invite => invite.RedeemedAt, redeemedAt), cancellationToken) == 1;

    public Task<InviteCode?> FindByHashAsync(string codeHash, CancellationToken cancellationToken) =>
        db.InviteCodes.AsNoTracking().FirstOrDefaultAsync(invite => invite.CodeHash == codeHash, cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}