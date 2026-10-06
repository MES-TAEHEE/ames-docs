using System.Security.Claims;
using AMES.Data.Repositories;
using AMES.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 로그인 처리 — 내부와 외부는 완전히 분리한다(09-26). 내부 로그인(/Account/Login)은 내부 Identity 계정만,
/// 외부 로그인(/portal/login)은 SCM-004 에서 등록한 외부 사용자(SCM_PortalVendorUser)만 받는다. 반대쪽 계정은 조회하지 않고
/// 일반 실패(Auth.Err.Invalid)로 거부한다 — 어느 쪽 계정이 존재하는지 드러내지 않는다. 외부 사용자는 AspNet 테이블과 무관하다.
/// 화면은 결과의 Redirect 로 이동하거나 ErrorKey(리소스 키)를 띄우기만 한다.
/// </summary>
public sealed class WebSignIn(
    SignInManager<ApplicationUser> signIn,
    UserManager<ApplicationUser> users,
    AuthRepository authRepo,
    ScmRepository scm,
    AuditLogger audit,
    ILogger<WebSignIn> logger)
{
    public sealed record Result(string? Redirect, string? ErrorKey)
    {
        public bool Succeeded => Redirect is not null && ErrorKey is null;
    }

    /// <summary>내부 로그인 — 내부 Identity 계정만. 외부 사용자 이메일은 Identity 에 없으므로 일반 실패가 된다.</summary>
    public async Task<Result> SignInInternalAsync(HttpContext ctx, string email, string password, bool rememberMe, string? returnUrl)
    {
        email = email.Trim();
        var user = await users.FindByEmailAsync(email);
        if (user is not null)
        {
            var (accountStatus, _) = authRepo.GetProfileStatus(user.Id);
            if (string.Equals(accountStatus, "LOCKED", StringComparison.OrdinalIgnoreCase)) return Error("Auth.Err.Locked");
            // 계정은 SYS-001 에서 관리자만 만든다 — 상태가 ACTIVE 일 때만 로그인(비활성·정지·프로필 없음은 거부, 10-07 메일 인증 단계 폐지)
            if (!string.Equals(accountStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)) return Error("Auth.Err.Inactive");
        }

        var result = await signIn.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            logger.LogInformation("User logged in.");
            if (user is not null) authRepo.RecordSuccessfulLogin(user.Id);
            // 같은 사이트 경로만(Open Redirect 방지), 외부 화면 경로로도 돌려보내지 않는다(내부 계정은 외부 화면을 열 수 없다)
            var target = !LocalUrl.IsLocal(returnUrl) || PortalAuth.IsPortalPath(new PathString(returnUrl!.Split('?', '#')[0])) ? "/" : returnUrl!;
            return new(target, null);
        }
        if (result.RequiresTwoFactor)
            return new($"/Account/LoginWith2fa?returnUrl={Uri.EscapeDataString(LocalUrl.OrDefault(returnUrl, ""))}&rememberMe={rememberMe.ToString().ToLower()}", null);
        if (result.IsLockedOut) return new("/Account/Lockout", null);
        if (result.IsNotAllowed) return Error("Auth.Err.Inactive");

        // 도메인 실패 카운터 — 5번째에 잠근다
        if (user is not null && authRepo.IncrementFailedCount(user.Id)) return Error("Auth.Err.LockedAfter5");
        return Error("Auth.Err.Invalid");
    }

    /// <summary>
    /// 외부 로그인 — 관리자가 SCM-004 에 등록한 외부 사용자만. 5회 실패하면 잠기고 내부 사용자가 SCM-004 에서 푼다.
    /// 성공하면 외부 쿠키를 발급한다(내부 쿠키는 그대로 — 한 브라우저에서 내부·외부 동시 로그인) — 역할 클레임은 없다(포탈 화면은 RBAC 대상이 아니다).
    /// </summary>
    public async Task<Result> SignInPortalAsync(HttpContext ctx, string email, string password, string? returnUrl)
    {
        var user = scm.FindPortalUser(email.Trim());
        if (user is null) return Error("Auth.Err.Invalid");
        if (!user.ActiveFlag || !user.VendorActive) return PortalFail(user, "Inactive", "Auth.Err.Invalid");
        if (user.LockedFlag) return PortalFail(user, "Locked", "Auth.Err.Locked");

        if (!PortalAuth.VerifyPassword(user.PasswordHash, password))
        {
            var nowLocked = scm.RecordPortalLoginFailure(user.UserID);
            return PortalFail(user, "InvalidPassword", nowLocked ? "Auth.Err.LockedAfter5" : "Auth.Err.Invalid");
        }
        scm.RecordPortalLoginSuccess(user.UserID);

        var portalIdentity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserID),
            new Claim(ClaimTypes.Name, user.UserID),
            new Claim(ActorCode.ClaimType, ActorCode.Normalize(user.UserID)),
            new Claim(PortalAuth.VendorClaim, user.VendorID),
        ], PortalAuth.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        await ctx.SignInAsync(PortalAuth.Scheme, new ClaimsPrincipal(portalIdentity), new AuthenticationProperties { IsPersistent = false });

        audit.Log("PORTAL", "LOGIN", "SCM_PortalVendorUser", user.UserID, null, new { user.UserID, user.VendorID }, actor: ActorCode.Normalize(user.UserID));
        logger.LogInformation("Portal user logged in: {Email} ({Vendor})", user.UserID, user.VendorID);

        // 같은 사이트의 외부 화면 경로만 — "/" 로 시작하지 않는 값은 PathString 이 예외를 내므로 먼저 거른다
        var target = LocalUrl.IsLocal(returnUrl) && PortalAuth.IsPortalPath(new PathString(returnUrl!.Split('?', '#')[0]))
            ? returnUrl!
            : PortalAuth.HomePath;
        return new(target, null);
    }

    Result PortalFail(ScmRepository.PortalUserRow user, string reason, string errorKey)
    {
        try { audit.Log("PORTAL", "LOGIN_FAIL", "SCM_PortalVendorUser", user.UserID, null, new { user.UserID, reason }, actor: ActorCode.Normalize(user.UserID)); } catch { }
        return Error(errorKey);
    }

    static Result Error(string key) => new(null, key);
}
