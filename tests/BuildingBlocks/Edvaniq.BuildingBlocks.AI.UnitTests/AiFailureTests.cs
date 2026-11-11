using System.Net;
using System.Net.Http.Headers;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// What may pass is worth asking again later, what stays (a wrong key, a refused request) is not.
public class AiFailureTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.PaymentRequired, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void Status_TellsWhetherItMayPass(HttpStatusCode status, bool transient) =>
        Assert.Equal(transient, AiFailure.IsTransient(new AiProviderException(status, null)));

    [Fact]
    public void TimeoutAndNetwork_MayPass_ABrokenAnswerDoesNot()
    {
        Assert.True(AiFailure.IsTransient(new TaskCanceledException()));
        Assert.True(AiFailure.IsTransient(new TimeoutException()));
        Assert.True(AiFailure.IsTransient(new HttpRequestException("no route")));
        Assert.True(AiFailure.IsTransient(new IOException("reset")));
        Assert.False(AiFailure.IsTransient(new InvalidOperationException("The AI provider sent no answer.")));
    }

    [Fact]
    public void RetryAfter_AsSecondsOrDate_OrNothing()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), AiFailure.RetryAfter(Refused(new RetryConditionHeaderValue(TimeSpan.FromSeconds(20))), Now));
        Assert.Equal(TimeSpan.FromMinutes(3), AiFailure.RetryAfter(Refused(new RetryConditionHeaderValue(new DateTimeOffset(Now.AddMinutes(3)))), Now));
        Assert.Equal(TimeSpan.Zero, AiFailure.RetryAfter(Refused(new RetryConditionHeaderValue(new DateTimeOffset(Now.AddMinutes(-3)))), Now));
        Assert.Null(AiFailure.RetryAfter(Refused(null), Now));
        Assert.Null(AiFailure.RetryAfter(new TaskCanceledException(), Now));
    }

    private static AiProviderException Refused(RetryConditionHeaderValue? retryAfter) => new(HttpStatusCode.TooManyRequests, retryAfter);
}