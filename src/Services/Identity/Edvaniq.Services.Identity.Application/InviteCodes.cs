using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

public interface IInviteCodes
{
    void Add(InviteCode invite);

    // Oldest first.
    Task<IReadOnlyList<InviteCode>> ListAsync(CancellationToken cancellationToken);

    Task<InviteCode?> FindByLabelAsync(string label, CancellationToken cancellationToken);

    // Marks the open code with this hash as redeemed in one statement, so of two redemptions at once only one succeeds.
    // False if no open code has the hash.
    Task<bool> TryMarkRedeemedAsync(string codeHash, DateTime redeemedAt, CancellationToken cancellationToken);

    Task<InviteCode?> FindByHashAsync(string codeHash, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

// The operator's invitation codes (#245) and their one redemption. No method ever returns a stored code, there is none:
// only Create hands out the codes it makes.
public sealed class InviteService(IInviteCodes invites, TimeProvider time)
{
    public const int MaxPerCreate = 100;

    public async Task<IReadOnlyList<(InviteCode Invite, string Code)>> CreateAsync(int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > MaxPerCreate)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"Between 1 and {MaxPerCreate} codes at once.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var made = Enumerable.Range(0, count).Select(_ => InviteCode.Create(now)).ToList();
        foreach (var (invite, _) in made)
        {
            invites.Add(invite);
        }

        await invites.SaveAsync(cancellationToken);
        return made;
    }

    public Task<IReadOnlyList<InviteCode>> ListAsync(CancellationToken cancellationToken) => invites.ListAsync(cancellationToken);

    public async Task<RevokeResult> RevokeAsync(string label, CancellationToken cancellationToken)
    {
        var invite = await invites.FindByLabelAsync(label.Trim().ToUpperInvariant(), cancellationToken);
        if (invite is null)
        {
            return RevokeResult.NotFound;
        }

        if (!invite.Revoke(time.GetUtcNow().UtcDateTime))
        {
            return invite.Status == InviteStatus.Redeemed ? RevokeResult.AlreadyRedeemed : RevokeResult.AlreadyRevoked;
        }

        await invites.SaveAsync(cancellationToken);
        return RevokeResult.Revoked;
    }

    // Registration (#249) calls this. Only an open code is redeemed, once.
    public async Task<RedeemResult> RedeemAsync(string? typed, CancellationToken cancellationToken)
    {
        if (InviteCode.Normalize(typed) is not { } code)
        {
            return RedeemResult.Unknown;
        }

        var hash = InviteCode.HashOf(code);
        if (await invites.TryMarkRedeemedAsync(hash, time.GetUtcNow().UtcDateTime, cancellationToken))
        {
            return RedeemResult.Redeemed;
        }

        return (await invites.FindByHashAsync(hash, cancellationToken))?.Status switch
        {
            InviteStatus.Redeemed => RedeemResult.AlreadyRedeemed,
            InviteStatus.Revoked => RedeemResult.Revoked,
            _ => RedeemResult.Unknown,
        };
    }
}

public enum RevokeResult
{
    Revoked,
    NotFound,
    AlreadyRedeemed,
    AlreadyRevoked,
}

public enum RedeemResult
{
    Redeemed,
    Unknown,
    AlreadyRedeemed,
    Revoked,
}