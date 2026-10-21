namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}