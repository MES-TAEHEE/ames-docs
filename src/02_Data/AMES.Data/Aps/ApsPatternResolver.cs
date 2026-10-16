namespace AMES.Data.Aps;

/// <summary>
/// 라인 → APS 가 쓸 가동 시간 패턴(2026-10-06 사용자 결정 (b)): MD_ApsLineStage.PatternID → APS_SETTING.DEFAULT_PATTERN 순이고
/// PP_LineSchedule 저장 패턴·라인 전용/전역 자동 해석보다 우선한다. 미지정·없는 패턴·ACTIVE 아님은 Resolve 가 null 을 주고 Problems 에
/// 라인마다 한 줄을 쌓는다 — 호출자(ApsRepository.BuildBundle · PpRepository.CreateApsWorkOrders)가 ApsConfigurationException 으로 막는다.
/// 순수 클래스 — 패턴 존재·상태 사전은 호출자가 DB 에서 읽어 넘긴다(정본 테스트 ApsSettingsLoaderTests).
/// </summary>
public sealed class ApsPatternResolver
{
    readonly ApsSettingsLoader.Parsed _parsed;
    readonly IReadOnlyDictionary<string, string?> _status;   // 존재하는 패턴만: PatternID → Status(NULL 은 ACTIVE 로 본다 — ReadDayCapacity 와 같다)
    readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _problems = new();

    public ApsPatternResolver(ApsSettingsLoader.Parsed parsed, IReadOnlyDictionary<string, string?> patternStatus)
    {
        _parsed = parsed;
        _status = patternStatus;
    }

    /// <summary>라인마다 한 줄, 처음 물은 순서. 비어 있으면 지금까지 물은 라인은 전부 유효.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>유효한 패턴 ID, 아니면 null(+ Problems 1줄). 같은 라인은 한 번만 판정한다.</summary>
    public string? Resolve(string lineId)
    {
        if (_cache.TryGetValue(lineId, out var cached)) return cached;
        var pat = _parsed.EffectivePattern(lineId);
        string? ok = null;
        if (pat is null)
            _problems.Add($"{lineId}: 가동 시간 패턴이 지정되지 않았습니다 (APS 설정의 라인 지정 또는 기본 패턴 DEFAULT_PATTERN).");
        else if (!_status.TryGetValue(pat, out var st))
            _problems.Add($"{lineId}: 지정한 가동 시간 패턴 {pat} 이 없습니다.");
        else if (!string.Equals(st ?? "ACTIVE", "ACTIVE", StringComparison.OrdinalIgnoreCase))
            _problems.Add($"{lineId}: 지정한 가동 시간 패턴 {pat} 은 {st} 상태라 쓸 수 없습니다.");
        else
            ok = pat;
        return _cache[lineId] = ok;
    }
}
