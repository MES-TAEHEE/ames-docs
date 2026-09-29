using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using AMES.Data.Repositories;
using AMES.Web.Data;
using AMES.Web.Services;

namespace AMES.Web.Components.Account;

// This is a server-side AuthenticationStateProvider that revalidates the security stamp for the connected user
// every 30 minutes an interactive circuit is connected.
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // Get the user manager from a new scope to ensure it fetches fresh data
        await using var scope = scopeFactory.CreateAsyncScope();
        // 외부(포탈) 사용자는 Identity 에 없다 — Identity 로 검사하면 항상 실패해 30분마다 인증이 풀렸다
        if (PortalAuth.IsPortalUser(authenticationState.User))
            return ValidatePortalUser(scope.ServiceProvider.GetRequiredService<ScmRepository>(), authenticationState.User);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await ValidateSecurityStampAsync(userManager, authenticationState.User);
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
            && string.Equals(user.VendorID, vendor, StringComparison.OrdinalIgnoreCase);
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
