using System.Globalization;
using System.Security.Claims;
using AMES.Data.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// 내부 로그인 세션 재확인 규칙(10-07). 열린 화면(회로, IdentityRevalidatingAuthenticationStateProvider)과
/// 화면 이동·새로고침(내부 쿠키)이 같은 주기·같은 상태 기준으로 다시 확인한다.
/// </summary>
public static class AuthRevalidation
{
    const string CheckedKey = "ames:statusCheckedUtc";

    /// <summary>설정 Auth:RevalidationMinutes(기본 5, 1 미만은 5).</summary>
    public static TimeSpan Interval(IConfiguration configuration)
        => TimeSpan.FromMinutes(configuration.GetValue<int?>("Auth:RevalidationMinutes") is int m && m >= 1 ? m : 5);

    /// <summary>
    /// 세션을 유지할 계정 상태 — ACTIVE 와 LOCKED. LOCKED 를 끊지 않는 이유: 5회 실패 잠금은 남이 일부러 일으킬 수 있어
    /// 일하던 사람을 내쫓게 되고, 관리자가 SYS-001 에서 잠그면 보안 스탬프 갱신으로 이미 끊긴다. 새 로그인은 막힌다(WebSignIn).
    /// </summary>
    public static bool KeepsSession(string? status)
        => string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "LOCKED", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 내부 쿠키 검증 — 보안 스탬프 검사(Identity 기본) 뒤에 계정 상태를 주기마다 한 번 본다. DB 를 직접 고친 비활성·정지·프로필 삭제도 끊는다.
    /// 확인 시각은 쿠키 속성에 두므로 요청마다 DB 를 부르지 않는다.
    /// </summary>
    public static async Task ValidateCookieAsync(CookieValidatePrincipalContext ctx, TimeSpan interval)
    {
        if (ctx.Principal?.Identity?.IsAuthenticated != true) return;
        var now = DateTimeOffset.UtcNow;
        if (ctx.Properties.Items.TryGetValue(CheckedKey, out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
            && now - last < interval)
            return;

        var id = ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        string status;
        try { (status, _) = ctx.HttpContext.RequestServices.GetRequiredService<AuthRepository>().GetProfileStatus(id ?? ""); }
        catch { return; }   // DB 장애로 접속 중인 사용자를 전부 내보내지는 않는다(다음 요청에 다시 본다)

        if (id is null || !KeepsSession(status))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        ctx.Properties.Items[CheckedKey] = now.ToString("O", CultureInfo.InvariantCulture);
        ctx.ShouldRenew = true;
    }
}
