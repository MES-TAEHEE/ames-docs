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
/// 불러오기 전·실패 시에는 권한 없음으로 보고, 처음 읽기에 실패하면 화면 읽기 검사(<see cref="CanOpen"/>)도 닫는다.
/// 다시 읽기는 EnsureAsync 에서만 한다 — 화면 마스터·권한 변경 알림(ScreenCatalogNotifier.Version)이 있었거나
/// <see cref="MaxAge"/> 가 지났을 때(다른 서버·DB 직접 수정분) 스레드 풀에서 읽어 바꾸므로 조회(IsVisible 등)는 DB 를 기다리지 않는다.
/// </summary>
public sealed class PermissionService
{
    static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    // 권한을 못 읽었을 때도 여는 경로 — /unauthorized 까지 막으면 그리로 보내는 것이 되돌아와 반복된다
    static readonly string[] OpenWhenFailed = ["", "unauthorized", "error", "account"];

    private readonly SysRepository _sys;
    private readonly ScreenCatalogNotifier _notifier;
    private Dictionary<string, string>? _hrefLevel; // normalised href → "REA" string
    private List<string> _screenHrefs = new();      // 권한 대상 내부 화면 href (긴 것 먼저)
    private string[]? _roles;                       // 마지막으로 불러온 역할 — 다시 읽을 때 같은 역할로 읽는다
    private DateTime _loadedAt;
    private long _loadedVersion;
    private bool _loadFailed;                       // 처음 읽기 실패 — 다음 EnsureAsync 에서 다시 시도
    public bool LoadFailed => _loadFailed;
    private Task? _refreshing;                      // 진행 중인 다시 읽기(메뉴·홈이 같은 알림으로 동시에 불러도 한 번만)

    public PermissionService(SysRepository sys, ScreenCatalogNotifier notifier)
    {
        _sys = sys;
        _notifier = notifier;
    }

    // ── Initialise ──────────────────────────────────────────────────────────

    public async Task EnsureAsync(Task<AuthenticationState> authStateTask)
    {
        if (_hrefLevel is not null) { await RefreshIfStaleAsync(); return; }
        var state = await authStateTask;
        LoadNow(state.User.FindAll(ClaimTypes.Role).Select(c => c.Value));
    }

    public async Task EnsureAsync(AuthenticationStateProvider provider)
    {
        if (_hrefLevel is not null) { await RefreshIfStaleAsync(); return; }
        var state = await provider.GetAuthenticationStateAsync();
        LoadNow(state.User.FindAll(ClaimTypes.Role).Select(c => c.Value));
    }

    private sealed record Snapshot(List<string> Hrefs, Dictionary<string, string> Levels);

    /// DB 에서 화면 목록·역할 권한을 읽는다(실패하면 예외).
    private Snapshot Read(string[] roles)
    {
        var roleSet = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        var screens = _sys.ListScreens();
        // 외부 화면(PORTAL)은 RBAC 대상이 아니다
        var hrefs = screens
            .Where(s => !string.IsNullOrEmpty(s.HRef) && !string.Equals(s.ProcessCode, "PORTAL", StringComparison.OrdinalIgnoreCase))
            .Select(s => Key(s.HRef!))
            .Where(h => h.Length > 0)
            .Distinct()
            .OrderByDescending(h => h.Length)
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (roleSet.Count > 0)
        {
            var perms = _sys.ListRolePermissions();
            var codeToHref = screens
                .Where(s => !string.IsNullOrEmpty(s.HRef))
                .ToDictionary(s => s.ScreenCode, s => Key(s.HRef!), StringComparer.OrdinalIgnoreCase);

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
        }
        return new(hrefs, result);
    }

    // 다 읽은 뒤 한 번에 바꾼다 — 다시 읽는 동안 빈 캐시(권한 없음)나 이전 값이 섞여 보이지 않게
    private void Apply(Snapshot s, string[] roles, long version)
    {
        _screenHrefs   = s.Hrefs;
        _hrefLevel     = s.Levels;
        _roles         = roles;
        _loadedVersion = version;
        _loadedAt      = DateTime.UtcNow;
        _loadFailed    = false;
    }

    private void LoadNow(IEnumerable<string> roles)
    {
        var arr = roles.ToArray();
        var version = _notifier.Version;
        try { Apply(Read(arr), arr, version); }
        catch
        {
            _screenHrefs = new();
            _hrefLevel   = new(StringComparer.OrdinalIgnoreCase);
            _roles       = arr;
            _loadedAt    = DateTime.UtcNow;
            _loadFailed  = true;
        }
    }

    private bool IsStale => _roles is not null
        && (_loadFailed || _loadedVersion != _notifier.Version || DateTime.UtcNow - _loadedAt > MaxAge);

    private async Task RefreshIfStaleAsync()
    {
        if (!IsStale) return;
        if (_refreshing is { IsCompleted: false }) { await _refreshing; return; }
        _refreshing = RefreshAsync();
        await _refreshing;
    }

    private async Task RefreshAsync()
    {
        var roles = _roles!;
        var version = _notifier.Version;
        try
        {
            // DB 조회는 스레드 풀에서 — DB 가 느리거나 끊겨도 회로(화면) 스레드가 멈추지 않는다
            var snap = await Task.Run(() => Read(roles));
            Apply(snap, roles, version);
        }
        catch
        {
            // 직전 값 유지(처음 읽기 실패 상태면 그대로 닫힘), 다음 EnsureAsync·주기에 재시도
            _loadedAt = DateTime.UtcNow;
        }
    }

    private static string Key(string href) => href.Trim().Trim('/').ToLowerInvariant();

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
        if (_hrefLevel is null) return ""; // 불러오기 전 → 권한 없음
        return _hrefLevel.TryGetValue(Key(href), out var lvl) ? lvl : "";
    }

    /// 메뉴/화면 표시 여부 — R 포함 시 표시
    public bool IsVisible(string href) => GetLevel(href).Contains('R');

    /// 등록·수정·삭제 버튼 활성화 — E 포함 시 활성
    public bool CanEdit(string href) => GetLevel(href).Contains('E');

    /// 기타(승인·특수) 버튼 활성화 — A 포함 시 활성
    public bool CanApprove(string href) => GetLevel(href).Contains('A');

    /// CanApprove 의 alias (이전 CanFull 호출부 호환)
    public bool CanFull(string href) => CanApprove(href);

    /// <summary>
    /// 주소(앱 기준 상대 경로, 쿼리 포함 가능)를 열 수 있는가 — SYS_Screen 에 등록된 화면(하위 경로 포함)이면 R 권한이 있어야 하고,
    /// 등록되지 않은 경로(홈·/unauthorized 등)는 막지 않는다. 권한을 불러오지 못했으면 홈·/unauthorized·오류·계정 화면만 연다.
    /// EnsureAsync 뒤에 부른다.
    /// </summary>
    public bool CanOpen(string relativePath)
    {
        var path = Key(relativePath.Split('?', '#')[0]);
        if (_hrefLevel is null || _loadFailed)
            return OpenWhenFailed.Any(p => path == p || (p.Length > 0 && path.StartsWith(p + "/", StringComparison.Ordinal)));
        var href = _screenHrefs.FirstOrDefault(h => path == h || path.StartsWith(h + "/", StringComparison.Ordinal));
        return href is null || IsVisible(href);
    }

    /// <summary>
    /// 권한 변경 후 다음 EnsureAsync 에서 다시 읽게 표시한다 — 값은 비우지 않으므로 다시 읽을 때까지 빈 틈(권한 없음·전체 허용)이 없다.
    /// 같은 서버의 다른 세션까지 알리려면 ScreenCatalogNotifier.Notify() 를 부른다.
    /// </summary>
    public void Reset()
    {
        if (_roles is not null) _loadedVersion = -1;
    }
}
