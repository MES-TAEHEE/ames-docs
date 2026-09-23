using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>
/// 화면 표시용 행위자 이름 해석. CreatedBy/ModifiedBy/ApprovedBy/RequestedBy 에는 사번(행위자 코드)이 들어가므로
/// 목록·상세에서는 "이름 (사번)" 으로 보여 준다. 사전은 <see cref="AuthRepository.ListActorNames"/>(사번·GUID·사용자명·별칭 → 이름)이며
/// 5분 캐시. 사전에 없는 코드(seed·system·POP-SCAN 등)는 그대로 보여 준다.
/// </summary>
public sealed class ActorNames(AuthRepository auth)
{
    static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    readonly object _gate = new();
    Dictionary<string, string>? _map;
    DateTime _loadedAt;

    Dictionary<string, string> Map()
    {
        var m = _map;
        if (m is not null && DateTime.UtcNow - _loadedAt < Ttl) return m;
        lock (_gate)
        {
            if (_map is not null && DateTime.UtcNow - _loadedAt < Ttl) return _map;
            try { _map = auth.ListActorNames(); }
            catch { _map ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
            _loadedAt = DateTime.UtcNow;
            return _map;
        }
    }

    /// <summary>사전 갱신을 강제한다(사용자·작업자 등록 직후).</summary>
    public void Invalidate() { lock (_gate) { _map = null; } }

    /// <summary>이름만. 모르면 null.</summary>
    public string? Name(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : Map().TryGetValue(code.Trim(), out var n) ? n : null;

    /// <summary>"이름 (코드)" — 이름을 모르면 코드 그대로, 코드가 비면 빈 문자열.</summary>
    public string Display(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim();
        return Name(c) is { } n && !string.Equals(n, c, StringComparison.OrdinalIgnoreCase) ? $"{n} ({c})" : c;
    }
}
