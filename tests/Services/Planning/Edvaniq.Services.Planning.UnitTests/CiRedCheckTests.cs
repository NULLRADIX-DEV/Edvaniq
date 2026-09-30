namespace Edvaniq.Services.Planning.UnitTests;

// Throwaway: proves a red test build produces no images. Never merge.
public class CiRedCheckTests
{
    [Fact]
    public void FailsOnPurpose() => Assert.Fail("Red on purpose (#118)");
}
