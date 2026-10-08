using System.Net;

using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Every way the status endpoint can fail ends in "not reachable", only a cancel by the caller stays a cancel.
public sealed class HttpBackendStatusTests
{
    [Fact]
    public async Task Success_IsReachable()
    {
        var status = Status((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        Assert.True(await status.IsReachableAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ErrorStatus_IsNotReachable(HttpStatusCode code)
    {
        var status = Status((_, _) => Task.FromResult(new HttpResponseMessage(code)));

        Assert.False(await status.IsReachableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoConnection_IsNotReachable()
    {
        var status = Status((_, _) => throw new HttpRequestException("Connection refused"));

        Assert.False(await status.IsReachableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NoAnswerInTime_IsNotReachable()
    {
        var status = Status(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }, timeout: TimeSpan.FromMilliseconds(100));

        Assert.False(await status.IsReachableAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelByTheCaller_IsACancel()
    {
        var status = Status(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => status.IsReachableAsync(cancellation.Token));
    }

    [Fact]
    public async Task AsksTheStatusEndpointOfTheHost()
    {
        Uri? asked = null;
        var status = Status((request, _) =>
        {
            asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        await status.IsReachableAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("https://edvaniq.test/api/status"), asked);
    }

    private static HttpBackendStatus Status(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer, TimeSpan? timeout = null) =>
        new(new HttpClient(new Handler(answer))
        {
            BaseAddress = new Uri("https://edvaniq.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(100),
        });

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            answer(request, cancellationToken);
    }
}