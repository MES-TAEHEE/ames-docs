using System.Security.Claims;
using AMES.Data.Repositories;
using AMES.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 내부 로그인(/Account/Login)·외부 로그인(/portal/login) 공용 처리. 어느 화면에서 로그인하든 계정 종류로 갈린다 —
/// SCM_PortalVendorUser(SCM-004 에서 등록한 외부 사용자)에 있는 이메일이면 외부 쿠키(AmesPortal)를 받아 외부 화면으로,
/// 아니면 내부 Identity 쿠키를 받아 내부로. 외부 사용자는 AspNet 테이블과 무관하다.
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
    /// <param name="Portal">외부 쿠키로 로그인됐는지 — 내부 화면의 ID 기억 쿠키는 내부 로그인일 때만 쓴다.</param>
    public sealed record Result(string? Redirect, string? ErrorKey, bool Portal = false)
    {
        public bool Succeeded => Redirect is not null && ErrorKey is null;
    }

    public async Task<Result> SignInAsync(HttpContext ctx, string email, string password, bool rememberMe, string? returnUrl)
    {
        email = email.Trim();
        var portalUser = scm.FindPortalUser(email);
        if (portalUser is not null)
            return await PortalAsync(ctx, portalUser, password, returnUrl);
        var user = await users.FindByEmailAsync(email);
        return await InternalAsync(ctx, user, email, password, rememberMe, returnUrl);
    }

    async Task<Result> InternalAsync(HttpContext ctx, ApplicationUser? user, string email, string password, bool rememberMe, string? returnUrl)
    {
        if (user is not null)
        {
            var (accountStatus, _) = authRepo.GetProfileStatus(user.Id);
            if (string.Equals(accountStatus, "LOCKED", StringComparison.OrdinalIgnoreCase)) return Error("Auth.Err.Locked");
            // ① 이메일 자기인증 완료 여부
            if (!user.EmailConfirmed) return Error("Auth.Err.EmailNotConfirmed");
            // ② 관리자 승인(ACTIVE) 여부. 프로필 없음=Active 취급, 자기가입 직후 PENDING 은 승인 대기.
            if (!string.Equals(accountStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)) return Error("Auth.Err.Pending");
        }

        var result = await signIn.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            logger.LogInformation("User logged in.");
            if (user is not null) authRepo.RecordSuccessfulLogin(user.Id);
            // 같은 브라우저에 외부 포탈 쿠키가 남아 있으면 지운다(스킴 선택이 외부 쿠키 존재 여부로 갈린다)
            ctx.Response.Cookies.Delete(PortalAuth.CookieName);
            var target = string.IsNullOrEmpty(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) ? "/" : returnUrl;
            return new(target, null);
        }
        if (result.RequiresTwoFactor)
            return new($"/Account/LoginWith2fa?returnUrl={Uri.EscapeDataString(returnUrl ?? "")}&rememberMe={rememberMe.ToString().ToLower()}", null);
        if (result.IsLockedOut) return new("/Account/Lockout", null);
        if (result.IsNotAllowed) return Error("Auth.Err.EmailNotConfirmed");

        // 도메인 실패 카운터 — 5번째에 잠근다
        if (user is not null && authRepo.IncrementFailedCount(user.Id)) return Error("Auth.Err.LockedAfter5");
        return Error("Auth.Err.Invalid");
    }

    // 외부 사용자: 관리자가 SCM-004 에 등록한 계정만 쓴다. 5회 실패하면 잠기고 내부 사용자가 SCM-004 에서 푼다.
    // 성공하면 내부 쿠키를 지우고 외부 쿠키를 발급한다 — 역할 클레임은 없다(포탈 화면은 RBAC 대상이 아니다).
    async Task<Result> PortalAsync(HttpContext ctx, ScmRepository.PortalUserRow user, string password, string? returnUrl)
    {
        if (!user.ActiveFlag || !user.VendorActive) return PortalFail(user, "Inactive", "Auth.Err.Invalid");
        if (user.LockedFlag) return PortalFail(user, "Locked", "Auth.Err.Locked");

        if (!PortalAuth.VerifyPassword(user.PasswordHash, password))
        {
            var nowLocked = scm.RecordPortalLoginFailure(user.UserID);
            return PortalFail(user, "InvalidPassword", nowLocked ? "Auth.Err.LockedAfter5" : "Auth.Err.Invalid");
        }
        scm.RecordPortalLoginSuccess(user.UserID);

        // 한 브라우저에 두 쿠키가 같이 있으면 판별이 꼬인다
        await signIn.SignOutAsync();
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

        var target = string.IsNullOrWhiteSpace(returnUrl) || !PortalAuth.IsPortalPath(new PathString(returnUrl.Split('?', '#')[0]))
            ? PortalAuth.HomePath
            : returnUrl;
        return new(target, null, Portal: true);
    }

    Result PortalFail(ScmRepository.PortalUserRow user, string reason, string errorKey)
    {
        try { audit.Log("PORTAL", "LOGIN_FAIL", "SCM_PortalVendorUser", user.UserID, null, new { user.UserID, reason }, actor: ActorCode.Normalize(user.UserID)); } catch { }
        return Error(errorKey);
    }

    static Result Error(string key) => new(null, key);
}
