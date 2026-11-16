namespace AMES.Data.Services.Upload;

/// <summary>수주 업로드가 이미 작업지시(WO)가 발행된 수주와 겹치는 정도.</summary>
public enum WoConflictLevel
{
    None,
    /// <summary>WO 가 있고 아직 안 끝났다 — 수량·납기만 바뀌어 기존 WO 와 어긋날 수 있다. 새 수주 번호로 올리도록 알린다.</summary>
    Issued,
    /// <summary>끝난(완료·입고·마감) WO 가 있다 — 이 수주 행은 처리하지 않는다.</summary>
    Completed,
}

/// <summary>한 수주 라인에 연결된 WO 한 건(취소 제외).</summary>
public sealed record OrderWoConflict(string SoNumber, int? SoLineNo, string WoNumber, string WoStatus);

/// <summary>
/// PP-002 수동 업로드(엑셀) 전에 같은 수주 번호에 이미 WO 가 있는지 판정하는 규칙. 순수 함수. 정본 테스트 UploadWoConflictRulesTests.
/// 수동 업로드 화면에서만 쓴다 — PO Sync·API 가져오기는 고객사 변경분을 계속 받아야 하므로 막지 않는다.
/// </summary>
public static class UploadWoConflictRules
{
    public static string Key(string soNumber, int? soLineNo) => $"{soNumber}|{soLineNo?.ToString() ?? ""}";

    /// <summary>끝난 WO(완료·입고·마감)가 하나라도 있으면 Completed, 취소 아닌 WO 만 있으면 Issued.</summary>
    public static WoConflictLevel Classify(IEnumerable<string?> statuses)
    {
        var level = WoConflictLevel.None;
        foreach (var s in statuses)
        {
            if (string.Equals(s?.Trim(), "Cancelled", StringComparison.OrdinalIgnoreCase)) continue;
            if (WoStatusRules.IsFinished(s)) return WoConflictLevel.Completed;
            level = WoConflictLevel.Issued;
        }
        return level;
    }

    /// <summary>수주 라인(키)별 판정과 그 라인의 WO 번호 목록.</summary>
    public static Dictionary<string, (WoConflictLevel Level, IReadOnlyList<string> WoNumbers)> ByOrder(IEnumerable<OrderWoConflict> rows)
        => rows.GroupBy(r => Key(r.SoNumber, r.SoLineNo), StringComparer.OrdinalIgnoreCase)
               .ToDictionary(g => g.Key,
                             g => (Classify(g.Select(x => x.WoStatus)), (IReadOnlyList<string>)g.Select(x => x.WoNumber).Distinct().ToList()),
                             StringComparer.OrdinalIgnoreCase);
}
