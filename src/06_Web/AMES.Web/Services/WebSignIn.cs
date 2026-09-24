using System.Security.Claims;
using AMES.Data.Repositories;
using AMES.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 내부 로그인(/Account/Login)·외부 로그인(/portal/login) 공용 처리. 어느 화면에서 로그인하든 계정 종류로 갈린다 —
/// ExternalCustomer 역할이면 외부 쿠키(AmesPortal)를 받아 외부 화면으로, 아니면 내부 Identity 쿠키를 받아 내부로.
/// 화면은 결과의 Redirect 로 이동하거나 ErrorKey(리소스 키)를 띄우기만 한다.
/// </summary>
public sealed class WebSignIn(
    SignInManager<ApplicationUser> signIn,
    UserManager<ApplicationUser> users,
    AuthRepository authRepo,
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
        var user = await users.FindByEmailAsync(email);
        if (user is not null && await users.IsInRoleAsync(user, PortalAuth.Role))
            return await PortalAsync(ctx, user, email, password, returnUrl);
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

    // 외부 사용자: 이메일 확인·승인 단계 없이 관리자 등록 계정만 쓴다. 성공하면 내부 쿠키를 지우고 외부 쿠키를 발급한다.
    async Task<Result> PortalAsync(HttpContext ctx, ApplicationUser user, string email, string password, string? returnUrl)
    {
        var (accountStatus, _) = authRepo.GetProfileStatus(user.Id);
        if (string.Equals(accountStatus, "LOCKED", StringComparison.OrdinalIgnoreCase) || await users.IsLockedOutAsync(user))
            return PortalFail(user, email, "Locked", "Auth.Err.Locked");

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);   // Identity 잠금 정책은 내부와 같다
            return PortalFail(user, email, "InvalidPassword", "Auth.Err.Invalid");
        }
        await users.ResetAccessFailedCountAsync(user);

        // 한 브라우저에 두 쿠키가 같이 있으면 판별이 꼬인다
        await signIn.SignOutAsync();
        var identityPrincipal = await signIn.CreateUserPrincipalAsync(user);   // 역할 클레임 포함
        var portalIdentity = new ClaimsIdentity(identityPrincipal.Claims, PortalAuth.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        await ctx.SignInAsync(PortalAuth.Scheme, new ClaimsPrincipal(portalIdentity), new AuthenticationProperties { IsPersistent = false });

        authRepo.RecordSuccessfulLogin(user.Id);
        // 행위자는 사번(클레임 ames:actor) — CreatedBy varchar(20)
        audit.Log("PORTAL", "LOGIN", "AspNetUsers", email, null, new { email, portal = true }, actor: ActorCode.Of(new ClaimsPrincipal(portalIdentity), email));
        logger.LogInformation("Portal user logged in: {Email}", email);

        var target = string.IsNullOrWhiteSpace(returnUrl) || !PortalAuth.IsPortalPath(new PathString(returnUrl.Split('?', '#')[0]))
            ? PortalAuth.HomePath
            : returnUrl;
        return new(target, null, Portal: true);
    }

    Result PortalFail(ApplicationUser user, string email, string reason, string errorKey)
    {
        // 로그인 전이라 클레임이 없다 — 사번, 없으면 이메일(AuditLogger 가 @ 앞부분으로 줄인다)
        string actor = email;
        try { actor = authRepo.GetEmployeeNo(user.Id) ?? email; } catch { }
        try { audit.Log("PORTAL", "LOGIN_FAIL", "AspNetUsers", email, null, new { email, reason }, actor: actor); } catch { }
        return Error(errorKey);
    }

    static Result Error(string key) => new(null, key);
}
