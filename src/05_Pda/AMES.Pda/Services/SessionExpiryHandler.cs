using System.Net;

namespace AMES.Pda.Services;

public sealed class SessionExpiryHandler(AuthState auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && request.Headers.Authorization?.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == true
            && request.Headers.Authorization.Parameter == auth.Token)
            auth.SignOut();
        return response;
    }
}
