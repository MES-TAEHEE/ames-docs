using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;

namespace AMES.Web.Services;

/// <summary>
/// 화면 표시용 행위자 이름 해석. CreatedBy/ModifiedBy/ApprovedBy/RequestedBy 에는 사번(행위자 코드)이 들어가므로
/// 목록·상세에서는 "이름 (사번)" 으로 보여 준다. 사전은 <see cref="AuthRepository.ListActorNames"/>(사번·GUID·사용자명·별칭 → 이름)이며
/// 5분마다 백그라운드에서 다시 읽는다(실패하면 이전 사전을 쓰며 30초 뒤 재시도, DB 장애 중에는 건너뜀 — <see cref="RefreshingCache{T}"/>).
/// 사전에 없는 코드(seed·system·POP-SCAN 등)는 그대로 보여 준다. 사용자 ID(GUID)는 괄호 안을 사번으로 바꿔 보인다.
/// </summary>
public sealed class ActorNames
{
    sealed record Snapshot(Dictionary<string, string> Names, Dictionary<string, string> EmpNos);

    static readonly Dictionary<string, string> Empty = new(StringComparer.OrdinalIgnoreCase);
    readonly RefreshingCache<Snapshot> _cache;

    public ActorNames(AuthRepository auth)
    {
        _cache = new RefreshingCache<Snapshot>(prev =>
            {
                var names = auth.ListActorNames();
                Dictionary<string, string> empNos;
                try { empNos = auth.ListUserEmployeeNos(); }
                catch { empNos = prev?.EmpNos ?? new(StringComparer.OrdinalIgnoreCase); }
                return new Snapshot(names, empNos);
            },
            ttl: TimeSpan.FromMinutes(5), retry: TimeSpan.FromSeconds(30), isDown: () => DbHealth.IsDown);
    }

    /// <summary>사전 갱신을 강제한다(사용자·작업자 등록 직후) — 다시 읽는 동안은 지금 사전을 쓴다.</summary>
    public void Invalidate() => _cache.Invalidate();

    /// <summary>이름만. 모르면 null.</summary>
    public string? Name(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : (_cache.Get()?.Names ?? Empty).TryGetValue(code.Trim(), out var n) ? n : null;

    /// <summary>"이름 (코드)" — 이름을 모르면 코드 그대로, 코드가 비면 빈 문자열.</summary>
    public string Display(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim();
        var snap  = _cache.Get();
        var name  = snap is not null && snap.Names.TryGetValue(c, out var n) ? n : null;
        var shown = snap is not null && snap.EmpNos.TryGetValue(c, out var no) ? no : c;
        return name is { } nm && !string.Equals(nm, shown, StringComparison.OrdinalIgnoreCase) ? $"{nm} ({shown})" : shown;
    }
}
