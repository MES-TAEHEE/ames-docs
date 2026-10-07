using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 외부 개방 화면(/portal) 인증 상수·판정.
/// 외부 사용자는 SCM-004 에서 등록한 SCM_PortalVendorUser(이메일·비밀번호·협력업체)이며 AspNet 테이블과 무관하다.
/// 로그인하면 <see cref="Scheme"/> 쿠키(협력업체 클레임 <see cref="VendorClaim"/> 포함)를 받는다. 내부 Identity 쿠키와는 이름·스킴이 다르고,
/// 내부 화면의 기본 인가 정책은 내부 스킴만, 외부 화면의 PortalAccess 정책은 외부 스킴만 통과시킨다 — 내부·외부는 로그인도
/// 화면도 완전히 분리된다(09-26: 내부 계정의 포탈 미리보기·대리 처리·계정 전환 흐름 제거).
/// 포탈 화면은 RBAC 대상이 아니다 — PortalAccess 정책만 통과하면 열고, 데이터는 로그인한 외부 사용자의 협력업체로 걸러진다.
/// </summary>
public static class PortalAuth
{
    /// <summary>외부 쿠키 인증 스킴. Identity 의 "Identity.External"(OAuth 용) 과 헷갈리지 않게 별도 이름.</summary>
    public const string Scheme = "AmesPortal";
    /// <summary>경로로 스킴을 고르는 기본 스킴(/portal → <see cref="Scheme"/>, 그 밖 → Identity).</summary>
    public const string DynamicScheme = "AmesDynamic";
    /// <summary>외부 쿠키 이름. 내부 Identity 쿠키와 한 브라우저에 같이 있어도 된다.</summary>
    public const string CookieName = ".AMES.Portal";
    /// <summary>외부 화면의 Blazor 회로 주소(App.razor 가 외부 화면에서만 이 주소로 연결). 내부 화면은 기본 /_blazor.</summary>
    public const string HubPath = "/portal/_blazor";
    /// <summary>외부 사용자의 협력업체(SCM_PortalVendorUser.VendorID) 클레임.</summary>
    public const string VendorClaim = "ames:vendor";
    /// <summary>외부 화면 라우트 접두어.</summary>
    public const string Prefix = "/portal";
    public const string LoginPath = "/portal/login";
    public const string HomePath = "/portal";
    /// <summary>외부 화면 인가 정책 이름(폴더 _Imports 로 전 화면 적용).</summary>
    public const string Policy = "PortalAccess";

    /// <summary>
    /// 로그인 때의 비밀번호 버전(비밀번호 해시의 SHA-256 앞 16자). 쿠키 검증이 현재 해시와 비교해 다르면 끊는다 —
    /// 본인 변경·SCM-004 재설정 뒤 다른 PC 의 로그인이 다음 요청에 끊긴다(10-07, 내부의 보안 스탬프 역할). DB 컬럼은 따로 두지 않는다.
    /// 이 클레임이 없는 쿠키(배포 전 로그인)는 통과시킨다 — 다음 로그인부터 적용(사용자 결정).
    /// </summary>
    public const string PasswordVersionClaim = "ames:pwv";

    public static string PasswordVersion(string passwordHash)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passwordHash)))[..16];

    public static bool PasswordVersionMatches(ClaimsPrincipal? principal, string passwordHash)
        => principal?.FindFirst(PasswordVersionClaim)?.Value is not { } v || v == PasswordVersion(passwordHash);

    /// <summary>외부 사용자 로그인 쿠키의 사용자(로그인·비밀번호 변경 후 재발급 공용). 역할 클레임은 없다.</summary>
    public static ClaimsPrincipal BuildPrincipal(AMES.Data.Repositories.ScmRepository.PortalUserRow user)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserID),
            new Claim(ClaimTypes.Name, user.UserID),
            new Claim(ActorCode.ClaimType, ActorCode.Normalize(user.UserID)),
            new Claim(VendorClaim, user.VendorID),
            new Claim(PasswordVersionClaim, PasswordVersion(user.PasswordHash)),
        ], Scheme, ClaimTypes.Name, ClaimTypes.Role));

    public static bool IsPortalUser(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
        && string.Equals(user.Identity.AuthenticationType, Scheme, StringComparison.Ordinal);

    public static bool IsInternalUser(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true
        && string.Equals(user.Identity.AuthenticationType, IdentityConstants.ApplicationScheme, StringComparison.Ordinal);

    /// <summary>외부 화면 정책: 외부 쿠키 사용자(SCM_PortalVendorUser)만 통과. 내부 계정은 외부 화면을 열 수 없다.</summary>
    public static bool AllowsPortalAccess(ClaimsPrincipal? user) => IsPortalUser(user);

    // 외부 사용자 비밀번호 — ASP.NET Identity 와 같은 해시 형식(V3, 솔트 포함)이라 기존 해시도 그대로 검증된다
    sealed class PortalPasswordUser { }
    static readonly PasswordHasher<PortalPasswordUser> Hasher = new();
    public static string HashPassword(string password) => Hasher.HashPassword(new(), password);
    public static bool VerifyPassword(string hash, string password)
    {
        try { return Hasher.VerifyHashedPassword(new(), hash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }

    public static bool IsPortalPath(PathString path)
        => path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>로그인 제출 — 내부 /Account/Login(2FA·복구 코드 화면 포함)·외부 /portal/login 의 POST. 요청 횟수 제한 대상.</summary>
    public static bool IsLoginPost(HttpRequest req)
        => HttpMethods.IsPost(req.Method)
           && ((req.Path.Value ?? "").StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase)
               || req.Path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase));

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
