using System.Security.Claims;
using AMES.Data.Repositories;
using AMES.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AMES.Web.Services;

/// <summary>
/// 로그인 시 Identity 클레임에 행위자 코드(<see cref="ActorCode.ClaimType"/> = SYS_UserProfile.EmployeeNo)를 얹는다.
/// 화면·서비스는 <see cref="ActorCode.Of"/> 로 이 값을 읽어 CreatedBy/ModifiedBy(varchar(20))에 넣는다.
/// 프로필이 없거나 사번이 비어 있으면 클레임을 넣지 않고 ActorCode 가 사용자명으로 폴백한다. 기존 쿠키는 재로그인해야 클레임이 생긴다.
/// </summary>
public sealed class AmesClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    AuthRepository auth)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        string? employeeNo = null;
        try { employeeNo = auth.GetEmployeeNo(user.Id); } catch { /* 프로필 조회 실패는 로그인을 막지 않는다 */ }
        if (!string.IsNullOrWhiteSpace(employeeNo))
            identity.AddClaim(new Claim(ActorCode.ClaimType, employeeNo.Trim()));
        return identity;
    }
}
