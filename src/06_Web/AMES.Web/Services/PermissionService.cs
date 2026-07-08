using System.Security.Claims;
using AMES.Data.Repositories;
using Microsoft.AspNetCore.Components.Authorization;

namespace AMES.Web.Services;

/// <summary>
/// Per-circuit permission cache.
/// PermissionLevel 값은 "R" / "RE" / "REA" 조합 문자열.
///   R — 화면 표시 + 조회조건 활성
///   E — 등록/수정/삭제 버튼 활성
///   A — 기타(승인·특수) 버튼 활성
/// Call EnsureAsync once (NavMenu or first page), then query synchronously.
/// </summary>
public sealed class PermissionService
{
    private readonly SysRepository _sys;
    private Dictionary<string, string>? _hrefLevel; // normalised href → "REA" string

    public PermissionService(SysRepository sys) => _sys = sys;

    // ── Initialise ──────────────────────────────────────────────────────────

    public async Task EnsureAsync(Task<AuthenticationState> authStateTask)
    {
        if (_hrefLevel is not null) return;
        var state = await authStateTask;
        Load(state.User.FindAll(ClaimTypes.Role).Select(c => c.Value));
    }

    public async Task EnsureAsync(AuthenticationStateProvider provider)
    {
        if (_hrefLevel is not null) return;
        var state = await provider.GetAuthenticationStateAsync();
        Load(state.User.FindAll(ClaimTypes.Role).Select(c => c.Value));
    }

    private void Load(IEnumerable<string> roles)
    {
        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        if (roleSet.Count == 0) { _hrefLevel = new(); return; }

        try
        {
            var perms   = _sys.ListRolePermissions();
            var screens = _sys.ListScreens();

            var codeToHref = screens
                .Where(s => !string.IsNullOrEmpty(s.HRef))
                .ToDictionary(
                    s => s.ScreenCode,
                    s => s.HRef!.TrimStart('/').ToLowerInvariant(),
                    StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in perms)
            {
                if (p.RoleName is null || !roleSet.Contains(p.RoleName)) continue;
                if (p.ScreenCode is null) continue;
                if (!codeToHref.TryGetValue(p.ScreenCode, out var href)) continue;

                // 여러 역할 보유 시 문자 합집합 (R+E = RE)
                var letters = Normalize(p.PermissionLevel);
                result[href] = result.TryGetValue(href, out var cur)
                    ? Merge(cur, letters)
                    : letters;
            }
            _hrefLevel = result;
        }
        catch
        {
            _hrefLevel = new();
        }
    }

    /// 저장된 값에서 유효 문자(R/E/A)만 추출, REA 순 정렬
    private static string Normalize(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return "";
        return new string(level.ToUpperInvariant()
                               .Where(c => c == 'R' || c == 'E' || c == 'A')
                               .Distinct()
                               .OrderBy(c => "REA".IndexOf(c))
                               .ToArray());
    }

    /// 두 레벨 문자열의 합집합, REA 순 정렬
    private static string Merge(string a, string b) =>
        new string((a + b).ToUpperInvariant()
                          .Where(c => c == 'R' || c == 'E' || c == 'A')
                          .Distinct()
                          .OrderBy(c => "REA".IndexOf(c))
                          .ToArray());

    // ── Query ────────────────────────────────────────────────────────────────

    private string GetLevel(string href)
    {
        if (_hrefLevel is null) return "REA"; // 미초기화 → 허용
        var key = href.TrimStart('/').ToLowerInvariant();
        return _hrefLevel.TryGetValue(key, out var lvl) ? lvl : "";
    }

    /// 메뉴/화면 표시 여부 — R 포함 시 표시
    public bool IsVisible(string href) => GetLevel(href).Contains('R');

    /// 등록·수정·삭제 버튼 활성화 — E 포함 시 활성
    public bool CanEdit(string href) => GetLevel(href).Contains('E');

    /// 기타(승인·특수) 버튼 활성화 — A 포함 시 활성
    public bool CanApprove(string href) => GetLevel(href).Contains('A');

    /// CanApprove 의 alias (이전 CanFull 호출부 호환)
    public bool CanFull(string href) => CanApprove(href);

    /// 권한 변경 후 캐시 무효화 — 다음 EnsureAsync 호출 시 DB에서 재조회
    public void Reset() => _hrefLevel = null;
}
