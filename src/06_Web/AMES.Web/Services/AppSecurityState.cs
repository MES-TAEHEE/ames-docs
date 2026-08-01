using AMES.Data.Repositories;
using AMES.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace AMES.Web.Services;

/// <summary>
/// SYS_Config <c>PASSWORD_MIN_LEN</c> 캐시 — 비밀번호 최소 길이 검증용.
/// Config 저장 시 <see cref="Invalidate"/> 로 갱신(앱 재시작 없이 다음 검증부터 반영).
/// </summary>
public sealed class AppSecurityState
{
    const string Key = "PASSWORD_MIN_LEN";
    readonly SysRepository _sys;
    readonly object _lock = new();
    int? _cache;

    public AppSecurityState(SysRepository sys) => _sys = sys;

    public int PasswordMinLen
    {
        get
        {
            lock (_lock)
            {
                if (_cache is { } c) return c;
                int v;
                try { v = _sys.GetConfigInt(Key, 6); } catch { v = 6; }  // DB 미가용 시 기본 6
                _cache = v;
                return v;
            }
        }
    }

    public void Invalidate() { lock (_lock) _cache = null; }
}

/// <summary>
/// 비밀번호 최소 길이를 SYS_Config(PASSWORD_MIN_LEN) 기준으로 동적 검증.
/// UserManager 의 모든 비밀번호 경로(Create/ChangePassword/ResetPassword/AddPassword)에 적용.
/// </summary>
public sealed class ConfigPasswordValidator : IPasswordValidator<ApplicationUser>
{
    readonly AppSecurityState _sec;
    public ConfigPasswordValidator(AppSecurityState sec) => _sec = sec;

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        int min = _sec.PasswordMinLen;
        if ((password?.Length ?? 0) < min)
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code        = "PasswordTooShort",
                Description = $"비밀번호는 최소 {min}자 이상이어야 합니다. / Password must be at least {min} characters."
            }));
        return Task.FromResult(IdentityResult.Success);
    }
}
