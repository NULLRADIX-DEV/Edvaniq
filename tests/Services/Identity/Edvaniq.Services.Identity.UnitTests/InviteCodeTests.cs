using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.UnitTests;

public sealed class InviteCodeTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_GivesAReadableCode_AndKeepsOnlyItsHash()
    {
        var (invite, code) = InviteCode.Create(Now);

        Assert.Matches(@"^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", code);
        Assert.Matches(@"^[0-9A-HJKMNP-TV-Z]{6}$", invite.Label);
        Assert.Equal(InviteCode.HashOf(InviteCode.Normalize(code)!), invite.CodeHash);
        Assert.DoesNotContain(code.Replace("-", "", StringComparison.Ordinal), invite.CodeHash, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((InviteStatus.Open, Now), (invite.Status, invite.CreatedAt));
    }

    [Fact]
    public void Codes_AreNotRepeated() =>
        Assert.Equal(1_000, Enumerable.Range(0, 1_000).Select(_ => InviteCode.Create(Now).Code).Distinct().Count());

    // What a person types from a sheet: any case, with or without dashes and spaces, O for 0, I or L for 1.
    [Theory]
    [InlineData("K7QX-4M2P-9ZTB", "K7QX4M2P9ZTB")]
    [InlineData("k7qx 4m2p 9ztb", "K7QX4M2P9ZTB")]
    [InlineData("1OIL-0000-1111", "101100001111")]
    [InlineData("K7QX-4M2P-9ZT", null)]
    [InlineData("K7QX-4M2P-9ZTBA", null)]
    [InlineData("K7QX-4M2P-9ZTU", null)]
    [InlineData("K7QX_4M2P_9ZTB", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_ReadsWhatPeopleType(string? typed, string? normalized) =>
        Assert.Equal(normalized, InviteCode.Normalize(typed));

    [Fact]
    public void Revoke_OnlyAnOpenCode()
    {
        var (invite, _) = InviteCode.Create(Now);

        Assert.True(invite.Revoke(Now.AddHours(1)));
        Assert.False(invite.Revoke(Now.AddHours(2)));
        Assert.Equal((InviteStatus.Revoked, (DateTime?)Now.AddHours(1)), (invite.Status, invite.RevokedAt));
    }
}