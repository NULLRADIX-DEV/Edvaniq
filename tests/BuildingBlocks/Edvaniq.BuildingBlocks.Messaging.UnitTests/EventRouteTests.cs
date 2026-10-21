using Edvaniq.Services.Alpha.Contracts;
using Edvaniq.Services.Beta.Contracts.Sub;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// An event goes to the stream of its service, found from the namespace of its type.
public class EventRouteTests
{
    [Fact]
    public void Event_GoesToTheStreamOfItsServiceWithItsOwnSubject()
    {
        var route = EventRoute.For(typeof(Pinged));

        Assert.Equal(new EventRoute("alpha", "ALPHA", "alpha.pinged"), route);
    }

    [Fact]
    public void Event_BelowAFolderOfContracts_StillBelongsToItsService()
    {
        var route = EventRoute.For(typeof(Nested));

        Assert.Equal(new EventRoute("beta", "BETA", "beta.nested"), route);
    }

    [Fact]
    public void Type_OutsideTheContractsOfAService_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => EventRoute.For(typeof(Stray)));

        Assert.Contains(typeof(Stray).FullName!, exception.Message);
    }

    [Fact]
    public void StreamAndSubjects_OfAService_FollowItsName()
    {
        Assert.Equal("LEARNINGENGINE", EventRoute.StreamOf("learningengine"));
        Assert.Equal("learningengine.>", EventRoute.SubjectsOf("LearningEngine"));
    }
}