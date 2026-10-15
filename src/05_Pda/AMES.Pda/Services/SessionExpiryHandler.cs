using System.Net;

namespace AMES.Pda.Services;

public sealed class SessionExpiryHandler(AuthState auth, SessionValidator validator) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = request.Headers.Authorization?.Parameter;
        var isLogin = request.RequestUri?.AbsolutePath is "/api/auth/login" or "/api/auth/barcode-login";
        if (!isLogin && auth.SessionCheckRequired)
        {
            await validator.CheckAsync();
            if (auth.Token is null || auth.Token != token)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (auth.SessionCheckRequired)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized
            && token is not null && !isLogin)
            auth.ExpireSession(token);
        return response;
    }
}
