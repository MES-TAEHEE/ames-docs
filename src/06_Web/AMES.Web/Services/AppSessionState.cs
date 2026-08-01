using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>
/// SYS_Config <c>SESSION_TIMEOUT_MIN</c>(분) 캐시 — 인증 쿠키 만료(ExpireTimeSpan) 결정용.
/// Config 저장 시 <see cref="Invalidate"/> + IOptionsMonitorCache 제거로 앱 재시작 없이 반영.
/// </summary>
public sealed class AppSessionState
{
    const string Key = "SESSION_TIMEOUT_MIN";
    readonly SysRepository _sys;
    readonly object _lock = new();
    int? _cache;

    public AppSessionState(SysRepository sys) => _sys = sys;

    public int Minutes
    {
        get
        {
            lock (_lock)
            {
                if (_cache is { } c) return c;
                int v;
                try { v = _sys.GetConfigInt(Key, 60); } catch { v = 60; }  // DB 미가용 시 기본 60분
                _cache = v;
                return v;
            }
        }
    }

    public void Invalidate() { lock (_lock) _cache = null; }
}
