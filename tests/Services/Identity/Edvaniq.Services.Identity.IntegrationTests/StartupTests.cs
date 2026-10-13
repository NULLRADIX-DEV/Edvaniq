using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.IntegrationTests;

// Identity refuses to start with an incomplete configuration instead of failing at the first token request. The
// message names the key, never its value.
public sealed class StartupTests
{
    [Theory]
    [InlineData("Tokens:Issuer", null, "Tokens:Issuer")]
    [InlineData("Tokens:Audience", null, "Tokens:Audience")]
    [InlineData("Tokens:SigningKey", null, "Tokens:SigningKey")]
    [InlineData("Tokens:SigningKey", "bm90IGEga2V5", "Tokens:SigningKey")]
    [InlineData("Tokens:Clients:web:Secret", null, "Tokens:Clients")]
    [InlineData("Tokens:Clients:web:Secret", "too-short", "Tokens:Clients:web:Secret")]
    public void IncompleteConfiguration_StopsTheStart(string key, string? value, string message)
    {
        using var identity = new IdentityFactory(new Dictionary<string, string?> { [key] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => identity.CreateClient());

        Assert.Contains(message, exception.Message);
        if (value is not null)
        {
            Assert.DoesNotContain(value, exception.Message);
        }
    }

    // A key from another curve or algorithm would sign tokens the services cannot check with ES256.
    [Fact]
    public void SigningKeyOfAnotherCurve_StopsTheStart()
    {
        using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        using var identity = new IdentityFactory(new Dictionary<string, string?>
        {
            ["Tokens:SigningKey"] = Convert.ToBase64String(other.ExportPkcs8PrivateKey()),
        });

        var exception = Assert.Throws<OptionsValidationException>(() => identity.CreateClient());

        Assert.Contains("Tokens:SigningKey", exception.Message);
    }
}