using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 외부 개방 화면(/portal) 인증 상수·판정.
/// 외부 사용자는 SCM-004 에서 등록한 SCM_PortalVendorUser(이메일·비밀번호·협력업체)이며 AspNet 테이블과 무관하다.
/// 로그인하면 <see cref="Scheme"/> 쿠키(협력업체 클레임 <see cref="VendorClaim"/> 포함)를 받는다. 내부 Identity 쿠키와는 이름·스킴이 다르고,
/// 내부 화면의 기본 인가 정책은 내부 스킴만 통과시키므로 두 세계가 서로 침범하지 않는다.
/// 포탈 화면은 RBAC 대상이 아니다 — 내부 사용자도 외부 사용자도 PortalAccess 정책만 통과하면 열고, 데이터는 협력업체로 걸러진다.
/// </summary>
public static class PortalAuth
{
    /// <summary>외부 쿠키 인증 스킴. Identity 의 "Identity.External"(OAuth 용) 과 헷갈리지 않게 별도 이름.</summary>
    public const string Scheme = "AmesPortal";
    /// <summary>쿠키 존재 여부로 스킴을 고르는 기본 스킴.</summary>
    public const string DynamicScheme = "AmesDynamic";
    /// <summary>외부 쿠키 이름. 경로는 "/" — Blazor 회로(/_blazor) 요청에도 실려야 한다.</summary>
    public const string CookieName = ".AMES.Portal";
    /// <summary>외부 사용자의 협력업체(SCM_PortalVendorUser.VendorID) 클레임.</summary>
    public const string VendorClaim = "ames:vendor";
    /// <summary>외부 화면 라우트 접두어.</summary>
    public const string Prefix = "/portal";
    public const string LoginPath = "/portal/login";
    public const string HomePath = "/portal";
    /// <summary>외부 화면 인가 정책 이름(폴더 _Imports 로 전 화면 적용).</summary>
    public const string Policy = "PortalAccess";

    public static bool IsPortalUser(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
        && string.Equals(user.Identity.AuthenticationType, Scheme, StringComparison.Ordinal);

    public static bool IsInternalUser(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
        && string.Equals(user.Identity.AuthenticationType, IdentityConstants.ApplicationScheme, StringComparison.Ordinal);

    /// <summary>외부 화면 정책: 내부 사용자와 외부 쿠키 사용자(SCM_PortalVendorUser) 모두 통과. 포탈 화면은 RBAC 대상이 아니다.</summary>
    public static bool AllowsPortalAccess(ClaimsPrincipal? user)
        => IsInternalUser(user) || IsPortalUser(user);

    // 외부 사용자 비밀번호 — ASP.NET Identity 와 같은 해시 형식(V3, 솔트 포함)이라 기존 해시도 그대로 검증된다
    sealed class PortalPasswordUser { }
    static readonly PasswordHasher<PortalPasswordUser> Hasher = new();
    public static string HashPassword(string password) => Hasher.HashPassword(new(), password);
    public static bool VerifyPassword(string hash, string password)
    {
        try { return Hasher.VerifyHashedPassword(new(), hash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }

    /// <summary>외부 로그아웃 뒤 내부 로그인의 ReturnUrl 로 넘길 수 있는지 — 같은 사이트의 절대 경로만(오픈 리디렉트 방지), 외부 화면은 제외.</summary>
    public static bool IsLocalReturnUrl(string? url)
        => !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !IsPortalPath(new PathString(url.Split('?', '#')[0]));

    public static bool IsPortalPath(PathString path)
        => path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>외부 호스트명으로 들어온 요청에 허용하는 경로 — 외부 화면·Blazor 회로·정적 파일·세션 유지 핑만.</summary>
    public static bool IsAllowedOnExternalHost(PathString path)
    {
        if (IsPortalPath(path)) return true;
        var p = path.Value ?? "/";
        if (p.StartsWith("/_", StringComparison.Ordinal)) return true;                       // /_blazor, /_framework, /_content
        if (p is "/keep-alive" or "/culture/set" or "/favicon.ico") return true;
        return Path.HasExtension(p) && !p.StartsWith("/Account", StringComparison.OrdinalIgnoreCase);   // css/js/images
    }
}
