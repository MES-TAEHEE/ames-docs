using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace AMES.Web.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Open Redirect 방지 — 같은 사이트 경로("/x")·앱 기준 상대 경로("Account/Manage")만 그대로 두고,
        // 같은 사이트의 절대 주소는 상대 경로로 바꾸며, 그 밖("//host"·외부 주소 등)은 홈으로 보낸다.
        // (템플릿의 IsWellFormedUriString(…, Relative) 검사는 "//host" 를 통과시켰다)
        if (!AMES.Web.Services.LocalUrl.IsLocal(uri) && !AMES.Web.Services.LocalUrl.IsAppRelative(uri))
        {
            try
            {
                var rel = navigationManager.ToBaseRelativePath(uri);
                uri = rel.Length == 0 || AMES.Web.Services.LocalUrl.IsAppRelative(rel) ? rel : "";
            }
            catch (ArgumentException) { uri = ""; }
        }
        if (uri.Length > 0 && !AMES.Web.Services.LocalUrl.ResolvesToSameSite(uri, navigationManager.BaseUri)) uri = "";

        // During static rendering, NavigateTo throws a NavigationException which is handled by the framework as a redirect.
        // So as long as this is called from a statically rendered Identity component, the InvalidOperationException is never thrown.
        navigationManager.NavigateTo(uri);
        throw new InvalidOperationException($"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
    }

    [DoesNotReturn]
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    [DoesNotReturn]
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    [DoesNotReturn]
    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    [DoesNotReturn]
    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);
}
