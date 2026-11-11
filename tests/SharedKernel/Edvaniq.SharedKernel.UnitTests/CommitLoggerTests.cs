using Edvaniq.ServiceDefaults;

namespace Edvaniq.SharedKernel.UnitTests;

// Every process with the service defaults logs its commit at startup, and something sensible without one.
public class CommitLoggerTests
{
    private const string Sha = "12ab162d3f4e5a6b7c8d9e0f1a2b3c4d5e6f7a8b";

    [Theory]
    [InlineData("1.0.0+" + Sha, Sha)]
    [InlineData("1.0.0", CommitLogger.Unknown)]
    [InlineData("1.0.0+abc123", CommitLogger.Unknown)]
    [InlineData("", CommitLogger.Unknown)]
    [InlineData(null, CommitLogger.Unknown)]
    public void CommitOf_TakesTheShaFromTheInformationalVersion(string? informationalVersion, string expected) =>
        Assert.Equal(expected, CommitLogger.CommitOf(informationalVersion));
}