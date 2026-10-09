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
    TrustedDevices devices,
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
    /// 신뢰 기기(<see cref="TrustedDevices"/>, 전에 로그인에 성공한 브라우저)는 계정이 잠겨도 맞는 비밀번호로 로그인되고, 그 기기에서 틀린 횟수는
    /// 계정이 아니라 기기에 센다(5회면 그 기기만 신뢰 해제) — 남이 다른 PC 에서 일부러 잠가도 평소 PC 에서는 계속 일한다.
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
        bool locked = string.Equals(accountStatus, "LOCKED", StringComparison.OrdinalIgnoreCase);
        var device = devices.Read(ctx, user.Id);
        if (!await users.CheckPasswordAsync(user, password ?? ""))
        {
            if (device is not null)
            {
                if (devices.RecordFailure(ctx, user.Id, device))
                    LogAccount(ctx, "DEVICE_UNTRUST", user.Email ?? email, "TrustedDeviceFailed5");
            }
            // ACTIVE 계정만 센다 — IncrementFailedCount 는 5회째에 상태를 LOCKED 로 덮으므로 비활성·정지 계정에 세면 상태가 바뀐다
            else if (active && authRepo.IncrementFailedCount(user.Id))
                LogAccount(ctx, "LOCK", user.Email ?? email, "FailedLogin5");
            return Error("Auth.Err.Invalid");
        }
        if (locked && device is null) return Error("Auth.Err.Locked");
        // 계정은 SYS-001 에서 관리자만 만든다 — 상태가 ACTIVE 일 때만 로그인(비활성·정지·프로필 없음은 거부, 10-07 메일 인증 단계 폐지).
        // LOCKED 는 신뢰 기기에서만 통과한다(계정 잠금은 그대로 남아 새 기기는 계속 막힌다)
        if (!active && !locked) return Error("Auth.Err.Inactive");

        var result = await signIn.PasswordSignInAsync(user, password!, rememberMe, lockoutOnFailure: false);
        // 2단계 인증은 쓰지 않는다(10-09 사용자 결정, 화면 삭제) — DB 에 TwoFactorEnabled 가 남은 계정도 비밀번호 확인으로 로그인을 마친다
        if (result.RequiresTwoFactor)
        {
            await ctx.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            await signIn.SignInAsync(user, rememberMe);
            result = SignInResult.Success;
        }
        if (result.Succeeded)
            devices.Trust(ctx, user.Id, device);
        if (result.Succeeded)
        {
            logger.LogInformation("User logged in.");
            authRepo.RecordSuccessfulLogin(user.Id);
            if (locked) LogAccount(ctx, "LOGIN_TRUSTED", user.Email ?? email, "LockedAccountTrustedDevice");
            // 같은 사이트 경로만(Open Redirect 방지), 외부 화면 경로로도 돌려보내지 않는다(내부 계정은 외부 화면을 열 수 없다)
            var target = !LocalUrl.IsLocal(returnUrl) || PortalAuth.IsPortalPath(new PathString(returnUrl!.Split('?', '#')[0])) ? "/" : returnUrl!;
            return new(target, null);
        }
        if (result.IsLockedOut) return new("/Account/Lockout", null);
        if (result.IsNotAllowed) return Error("Auth.Err.Inactive");
        return Error("Auth.Err.Invalid");
    }

    // 잠금은 남이 일부러 일으킬 수 있다(관리자 해제 전까지 새 기기는 못 쓴다) — 누가 어디서 잠갔는지 추적하도록 시각·IP 를 감사 로그에 남긴다.
    // 잠긴 계정의 신뢰 기기 로그인(LOGIN_TRUSTED)·기기 신뢰 해제(DEVICE_UNTRUST)도 같은 형식으로 남긴다
    void LogAccount(HttpContext ctx, string action, string key, string reason, string table = "AspNetUsers")
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        logger.LogWarning("Account security event {Action}: {Account} from {Ip} ({Reason})", action, key, ip, reason);
        try { audit.Log("ACCOUNT", action, table, key, null, new { Account = key, Ip = ip, Reason = reason }, actor: ActorCode.Normalize(key)); } catch { }
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
                LogAccount(ctx, "LOCK", user.UserID, "FailedLogin5", "SCM_PortalVendorUser");
            return PortalFail(user, "InvalidPassword", "Auth.Err.Invalid");
        }
        if (!usable) return PortalFail(user, "Inactive", "Auth.Err.Invalid");
        if (user.LockedFlag) return PortalFail(user, "Locked", "Auth.Err.Locked");
        scm.RecordPortalLoginSuccess(user.UserID);

        await ctx.SignInAsync(PortalAuth.Scheme, PortalAuth.BuildPrincipal(user), new AuthenticationProperties { IsPersistent = false });

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
