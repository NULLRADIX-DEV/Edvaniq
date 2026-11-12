using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.UnitTests;

public sealed class AccountEmailTests
{
    [Theory]
    [InlineData("anna@example.org", "anna@example.org")]
    [InlineData("  Anna@Example.ORG ", "anna@example.org")]
    [InlineData("a+b@x.de", "a+b@x.de")]
    [InlineData("anna@example", null)]
    [InlineData("anna example@x.de", null)]
    [InlineData("a@b@x.de", null)]
    [InlineData("@x.de", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_TrimsAndLowers_OrRefuses(string? email, string? normalized) =>
        Assert.Equal(normalized, AccountEmail.Normalize(email));

    [Fact]
    public void LongestAddress_IsTaken_OneMoreIsRefused()
    {
        var longest = new string('a', AccountEmail.MaxLength - 5) + "@x.de";

        Assert.Equal(longest, AccountEmail.Normalize(longest));
        Assert.Null(AccountEmail.Normalize("a" + longest));
    }

    [Fact]
    public void Fingerprint_HidesTheAddress_AndIsTheSameForTheSameAddress()
    {
        var fingerprint = AccountEmail.Fingerprint("anna@example.org");

        Assert.DoesNotContain("anna", fingerprint, StringComparison.Ordinal);
        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint, AccountEmail.Fingerprint("anna@example.org"));
        Assert.NotEqual(fingerprint, AccountEmail.Fingerprint("bob@example.org"));
    }
}