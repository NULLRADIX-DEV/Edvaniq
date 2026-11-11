using Microsoft.Net.Http.Headers;

namespace Edvaniq.Services.Content.Api;

// Hands the caller's own token on to the Planning client, so Planning answers for that user and nobody else. Only the
// token goes along, no other header of the request. Only that client gets this handler.
internal sealed class ForwardUserToken(IHttpContextAccessor context) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (context.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString() is { Length: > 0 } authorization)
        {
            request.Headers.Remove(HeaderNames.Authorization);
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
        }

        return base.SendAsync(request, cancellationToken);
    }
}