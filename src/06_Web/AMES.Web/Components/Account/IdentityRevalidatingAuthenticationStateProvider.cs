using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Web.Data;
using AMES.Web.Services;

namespace AMES.Web.Components.Account;

// 대화형 회로의 사용자를 주기적으로 다시 확인한다 — 보안 스탬프 + 계정 상태(SYS_UserProfile).
// SYS-001 이 상태·역할·비밀번호를 바꾸면 스탬프를 갱신하므로 열린 화면도 다음 확인 때 로그인으로 돌아간다(SessionGuard).
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> options,
        IConfiguration configuration)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    // 설정 Auth:RevalidationMinutes(기본 5, 1 미만은 5) — 관리자가 막은 계정이 열린 화면에서 최대 이 시간 안에 끊긴다
    protected override TimeSpan RevalidationInterval => AuthRevalidation.Interval(configuration);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // Get the user manager from a new scope to ensure it fetches fresh data
        await using var scope = scopeFactory.CreateAsyncScope();
        // 외부(포탈) 사용자는 Identity 에 없다 — Identity 로 검사하면 항상 실패해 30분마다 인증이 풀렸다
        if (PortalAuth.IsPortalUser(authenticationState.User))
            return ValidatePortalUser(scope.ServiceProvider.GetRequiredService<ScmRepository>(), authenticationState.User);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        bool stampOk;
        try { stampOk = await ValidateSecurityStampAsync(userManager, authenticationState.User); }
        // DB 연결 장애면 이번 확인은 건너뛰고 세션을 유지한다(다음 주기에 다시 본다). 예외를 그대로 두면 프레임워크가
        // "유효하지 않음"으로 보고 열린 화면을 로그아웃시켜, 장애가 이어지는 동안 접속자가 전부 쫓겨났다(10-09)
        catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { return true; }
        if (!stampOk) return false;
        return ValidateAccountStatus(scope.ServiceProvider.GetRequiredService<AuthRepository>(), authenticationState.User);
    }

    // DB 를 직접 고친 비활성·정지·프로필 삭제도 끊는다. LOCKED 는 끊지 않는다 — 5회 실패 잠금은 남이 일부러 일으킬 수 있어
    // 일하던 사람을 내쫓게 되고, 관리자가 SYS-001 에서 잠근 경우는 스탬프 갱신으로 이미 끊긴다
    private static bool ValidateAccountStatus(AuthRepository auth, ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return false;
        string status;
        try { (status, _) = auth.GetProfileStatus(id); }
        catch { return true; }   // DB 장애로 접속 중인 사용자를 전부 내보내지는 않는다
        return AuthRevalidation.KeepsSession(status);
    }

    // 포탈 쿠키 검증(Program.cs OnValidatePrincipal)과 같은 기준 — 비활성·잠금·업체 비활성·업체 변경·삭제면 끊는다
    private static bool ValidatePortalUser(ScmRepository scm, ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return false;
        var vendor = principal.FindFirstValue(PortalAuth.VendorClaim);
        ScmRepository.PortalUserRow? user;
        try { user = scm.FindPortalUser(id); }
        catch { return true; }   // DB 장애로 접속 중인 외부 사용자를 전부 내보내지는 않는다(쿠키 검증과 같은 판단)
        return user is not null && user.ActiveFlag && !user.LockedFlag && user.VendorActive
            && string.Equals(user.VendorID, vendor, StringComparison.OrdinalIgnoreCase)
            && PortalAuth.PasswordVersionMatches(principal, user.PasswordHash);
    }

    private async Task<bool> ValidateSecurityStampAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }
        else if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }
        else
        {
            var principalStamp = principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
            var userStamp = await userManager.GetSecurityStampAsync(user);
            return principalStamp == userStamp;
        }
    }
}
