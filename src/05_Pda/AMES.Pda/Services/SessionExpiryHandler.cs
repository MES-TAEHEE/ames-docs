using System.Net;

namespace AMES.Pda.Services;

public sealed class SessionExpiryHandler(AuthState auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = auth.Token;
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && token is not null && token == auth.Token
            && request.RequestUri?.AbsolutePath is not ("/api/auth/login" or "/api/auth/barcode-login"))
            auth.SignOut();
        return response;
    }
}
