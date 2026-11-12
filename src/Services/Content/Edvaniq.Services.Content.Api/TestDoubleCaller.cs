using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.Api;

// Reads the claim from the validated token only. Identity sets it for accounts the operator marked, no request field
// can.
internal sealed class TestDoubleCaller(IHttpContextAccessor accessor) : ITestDoubleCaller
{
    public const string Claim = "test_double";

    public bool UsesTestDouble =>
        accessor.HttpContext?.User.HasClaim(claim => claim.Type == Claim && claim.Value == "true") == true;
}