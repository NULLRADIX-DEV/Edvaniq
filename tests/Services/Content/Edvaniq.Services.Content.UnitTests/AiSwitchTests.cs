using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #305: the switch only changes, and only takes a new time, when it really changes.
public sealed class AiSwitchTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Set_ChangesOnlyWhenTheStateDiffers()
    {
        var aiSwitch = AiSwitch.Running(Now);

        Assert.False(aiSwitch.Set(false, Now.AddMinutes(1)));
        Assert.Equal((false, Now), (aiSwitch.Paused, aiSwitch.ChangedAt));
        Assert.True(aiSwitch.Set(true, Now.AddMinutes(2)));
        Assert.False(aiSwitch.Set(true, Now.AddMinutes(3)));
        Assert.Equal((true, Now.AddMinutes(2)), (aiSwitch.Paused, aiSwitch.ChangedAt));
        Assert.True(aiSwitch.Set(false, Now.AddMinutes(4)));
        Assert.Equal(AiSwitch.TheOne, aiSwitch.Id);
    }
}