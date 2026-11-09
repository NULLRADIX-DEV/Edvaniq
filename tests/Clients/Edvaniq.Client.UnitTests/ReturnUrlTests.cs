using Edvaniq.Client.UI.Shell;

namespace Edvaniq.Client.UnitTests;

// After the login the browser goes back to where the user came from, but only to a path of this site. Anything else
// would make the login page a way to send people to another site with our address in the link.
public sealed class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/systemstatus")]
    [InlineData("/systemstatus?x=1#oben")]
    [InlineData("/a/b")]
    public void PathOfThisSite_IsTheTarget(string returnUrl)
    {
        Assert.Equal(returnUrl, ReturnUrl.Target(returnUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("systemstatus")]
    [InlineData("//evil.example")]
    [InlineData("//evil.example/systemstatus")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("https://evil.example/")]
    [InlineData("http://localhost/systemstatus")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\tevil")]
    [InlineData(" /systemstatus")]
    public void AnythingElse_LeadsToTheStartPage(string? returnUrl)
    {
        Assert.Equal("/", ReturnUrl.Target(returnUrl));
    }

    [Fact]
    public void LoginFor_CarriesThePathEscaped()
    {
        Assert.Equal("login?returnUrl=%2Fsystemstatus%3Fx%3D1", ReturnUrl.LoginFor("/systemstatus?x=1"));
    }
}