using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 외부 개방 화면(/portal) 인증 상수·판정.
/// 외부 사용자는 SYS-001 에서 만든 Identity 계정에 <see cref="Role"/> 역할을 준 사람이며,
/// /portal/login 에서만 로그인해 <see cref="Scheme"/> 쿠키를 받는다. 내부 Identity 쿠키와는 이름·스킴이 다르고,
/// 내부 화면의 기본 인가 정책은 내부 스킴만 통과시키므로 두 세계가 서로 침범하지 않는다.
/// 내부 사용자는 자기 Identity 쿠키로 /portal 화면을 그대로 연다(SYS_RolePermission 으로 화면 권한).
/// </summary>
public static class PortalAuth
{
    /// <summary>외부 쿠키 인증 스킴. Identity 의 "Identity.External"(OAuth 용) 과 헷갈리지 않게 별도 이름.</summary>
    public const string Scheme = "AmesPortal";
    /// <summary>쿠키 존재 여부로 스킴을 고르는 기본 스킴.</summary>
    public const string DynamicScheme = "AmesDynamic";
    /// <summary>외부 쿠키 이름. 경로는 "/" — Blazor 회로(/_blazor) 요청에도 실려야 한다.</summary>
    public const string CookieName = ".AMES.Portal";
    /// <summary>외부 사용자 역할.</summary>
    public const string Role = "ExternalCustomer";
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

    /// <summary>외부 화면 정책: 내부 사용자는 무조건 통과(화면 권한은 PermissionService), 외부 쿠키는 역할이 있어야 통과.</summary>
    public static bool AllowsPortalAccess(ClaimsPrincipal? user)
        => IsInternalUser(user) || (IsPortalUser(user) && user!.IsInRole(Role));

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
