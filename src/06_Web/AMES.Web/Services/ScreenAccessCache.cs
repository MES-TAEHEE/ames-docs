using System.Collections.Concurrent;
using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>
/// 화면 목록(SYS_Screen)·역할 권한(SYS_RolePermission)·사용자 역할을 서버 전체에서 함께 쓰는 캐시(11-19 화면 로딩 개선).
/// 전에는 화면을 열 때마다(정적 레이아웃이라 요청마다 새 범위) 권한 판정·메뉴·상단 제목·TopBar 가 같은 목록을 각각 DB 에서 읽어
/// 한 번 이동에 SYS_Screen 3번, 역할 2~3번을 읽었다.
/// 화면 마스터·권한·역할을 바꾸면 각 화면이 <see cref="ScreenCatalogNotifier.Notify"/> 를 부르므로 버전이 바뀌면 바로 다시 읽고,
/// DB 를 직접 고친 경우(다른 서버 포함)는 <see cref="MaxAge"/> 안에 반영된다. 저장·승인 직전 권한 재확인은 fresh 로 캐시를 건너뛴다.
/// 반환 목록은 여러 세션이 함께 보므로 고치지 말 것(읽기 전용으로 쓴다).
/// </summary>
public sealed class ScreenAccessCache
{
    static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    sealed record Entry<T>(T Value, long Version, DateTime At);

    readonly SysRepository _sys;
    readonly ScreenCatalogNotifier _notifier;
    Entry<IReadOnlyList<SysRepository.ScreenRow>>? _screens;
    Entry<IReadOnlyList<SysRepository.RolePermRow>>? _perms;
    readonly ConcurrentDictionary<string, Entry<IReadOnlyList<(string Id, string Name)>>> _roles = new(StringComparer.OrdinalIgnoreCase);

    public ScreenAccessCache(SysRepository sys, ScreenCatalogNotifier notifier)
    {
        _sys = sys;
        _notifier = notifier;
    }

    /// <summary>전체 화면 목록(모든 모듈). 실패하면 예외 — 호출측이 지금처럼 처리한다.</summary>
    public IReadOnlyList<SysRepository.ScreenRow> Screens(bool fresh = false)
        => Get(ref _screens, () => _sys.ListScreens(), fresh);

    /// <summary>모듈 하나의 화면(예: "WEB") — ListScreens(module) 와 같은 결과(대소문자 무시 비교).</summary>
    public List<SysRepository.ScreenRow> Screens(string moduleCode, bool fresh = false)
        => Screens(fresh).Where(s => string.Equals(s.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<SysRepository.RolePermRow> RolePermissions(bool fresh = false)
        => Get(ref _perms, () => _sys.ListRolePermissions(), fresh);

    public IReadOnlyList<(string Id, string Name)> UserRoles(string userId, bool fresh = false)
    {
        var version = _notifier.Version;
        if (!fresh && _roles.TryGetValue(userId, out var e) && Valid(e.Version, e.At, version)) return e.Value;
        IReadOnlyList<(string Id, string Name)> value = _sys.ListUserRoles(userId);
        _roles[userId] = new(value, version, DateTime.UtcNow);
        return value;
    }

    T Get<T>(ref Entry<T>? slot, Func<T> load, bool fresh) where T : class
    {
        var version = _notifier.Version;
        var e = Volatile.Read(ref slot);
        if (!fresh && e is not null && Valid(e.Version, e.At, version)) return e.Value;
        var value = load();
        Volatile.Write(ref slot, new Entry<T>(value, version, DateTime.UtcNow));
        return value;
    }

    static bool Valid(long entryVersion, DateTime at, long version)
        => entryVersion == version && DateTime.UtcNow - at < MaxAge;
}
