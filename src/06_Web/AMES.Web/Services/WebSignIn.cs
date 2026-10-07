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

    // 없는 계정도 해시 검증 시간을 똑같이 쓰게 한다 — 응답 시간으로 계정 존재를 가늠하지 못하게
    static readonly string DummyHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString());

    /// <summary>
    /// 내부 로그인 — 내부 Identity 계정만. 외부 사용자 이메일은 Identity 에 없으므로 일반 실패가 된다.
    /// 비밀번호를 먼저 확인한다(10-07): 틀리면 계정 유무·상태와 관계없이 일반 실패만 보이고, 맞을 때만 잠김·비활성 사유를 보인다.
    /// 잠금(5회 실패, SYS_UserProfile LOCKED)은 자동으로 풀리지 않는다 — SYS-001 에서 Admin 이 해제한다(Identity 자체 잠금은 쓰지 않는다).
    /// </summary>
    public async Task<Result> SignInInternalAsync(HttpContext ctx, string email, string password, bool rememberMe, string? returnUrl)
    {
        email = email.Trim();
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyHash, password ?? "");
            return Error("Auth.Err.Invalid");
        }

        var (accountStatus, _) = authRepo.GetProfileStatus(user.Id);
        bool active = string.Equals(accountStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase);
        if (!await users.CheckPasswordAsync(user, password ?? ""))
        {
            // ACTIVE 계정만 센다 — IncrementFailedCount 는 5회째에 상태를 LOCKED 로 덮으므로 비활성·정지 계정에 세면 상태가 바뀐다
            if (active && authRepo.IncrementFailedCount(user.Id))
                LogLock(ctx, "AspNetUsers", user.Email ?? email);
            return Error("Auth.Err.Invalid");
        }
        if (string.Equals(accountStatus, "LOCKED", StringComparison.OrdinalIgnoreCase)) return Error("Auth.Err.Locked");
        // 계정은 SYS-001 에서 관리자만 만든다 — 상태가 ACTIVE 일 때만 로그인(비활성·정지·프로필 없음은 거부, 10-07 메일 인증 단계 폐지)
        if (!active) return Error("Auth.Err.Inactive");

        var result = await signIn.PasswordSignInAsync(user, password!, rememberMe, lockoutOnFailure: false);
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
        return Error("Auth.Err.Invalid");
    }

    // 잠금은 남이 일부러 일으킬 수 있다(관리자 해제 전까지 못 쓴다) — 누가 어디서 잠갔는지 추적하도록 시각·IP 를 감사 로그에 남긴다
    void LogLock(HttpContext ctx, string table, string key)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        logger.LogWarning("Account locked after repeated failures: {Account} from {Ip}", key, ip);
        try { audit.Log("ACCOUNT", "LOCK", table, key, null, new { Account = key, Ip = ip, Reason = "FailedLogin5" }, actor: ActorCode.Normalize(key)); } catch { }
    }

    /// <summary>
    /// 외부 로그인 — 관리자가 SCM-004 에 등록한 외부 사용자만. 5회 실패하면 잠기고 내부 사용자가 SCM-004 에서 푼다.
    /// 성공하면 외부 쿠키를 발급한다(내부 쿠키는 그대로 — 한 브라우저에서 내부·외부 동시 로그인) — 역할 클레임은 없다(포탈 화면은 RBAC 대상이 아니다).
    /// </summary>
    public async Task<Result> SignInPortalAsync(HttpContext ctx, string email, string password, string? returnUrl)
    {
        var user = scm.FindPortalUser(email.Trim());
        if (user is null)
        {
            PortalAuth.VerifyPassword(DummyHash, password);
            return Error("Auth.Err.Invalid");
        }

        // 내부 로그인과 같이 비밀번호를 먼저 본다 — 틀리면 상태와 관계없이 일반 실패(계정 존재·잠김 여부를 드러내지 않는다)
        bool usable = user.ActiveFlag && user.VendorActive;
        if (!PortalAuth.VerifyPassword(user.PasswordHash, password))
        {
            if (usable && !user.LockedFlag && scm.RecordPortalLoginFailure(user.UserID))
                LogLock(ctx, "SCM_PortalVendorUser", user.UserID);
            return PortalFail(user, "InvalidPassword", "Auth.Err.Invalid");
        }
        if (!usable) return PortalFail(user, "Inactive", "Auth.Err.Invalid");
        if (user.LockedFlag) return PortalFail(user, "Locked", "Auth.Err.Locked");
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
