using System.Globalization;

namespace AMES.Data.Services.Upload;

/// <summary>비교 한 줄: Kind = ADDED(새로 추가) · CHANGED(값 변경) · REMOVED(파일에 없음 → 저장 시 지워짐/취소).</summary>
public sealed record DiffLine(string Key, string? Old, string? New, string Kind);

public sealed record UploadDiff(IReadOnlyList<DiffLine> Lines, int Added, int Changed, int Removed, int Unchanged)
{
    /// <summary>기존 데이터와 다른 점이 하나라도 있는가. 없으면 저장할 필요가 없다(중복 업로드).</summary>
    public bool HasChanges => Added + Changed + Removed > 0;
}

/// <summary>
/// 업로드 전후 비교 규칙. 키(품번·날짜 또는 수주-라인)마다 기존 값과 파일 값을 문자열로 놓고 같은지 본다.
/// 값 문자열 형식은 여기서만 만든다 — 화면의 "이전 → 새 값"과 기존 스냅샷이 같은 형식이어야 비교가 맞는다.
/// 순수 함수. 정본 테스트 UploadDiffTests.
/// </summary>
public static class UploadDiffRules
{
    public const string Added = "ADDED";
    public const string Changed = "CHANGED";
    public const string Removed = "REMOVED";

    public static UploadDiff Compare(IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> incoming)
    {
        var lines = new List<DiffLine>();
        int added = 0, changed = 0, removed = 0, unchanged = 0;
        foreach (var key in existing.Keys.Union(incoming.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            bool hasOld = existing.TryGetValue(key, out var oldV);
            bool hasNew = incoming.TryGetValue(key, out var newV);
            if (hasOld && hasNew)
            {
                if (oldV == newV) { unchanged++; continue; }
                changed++;
                lines.Add(new DiffLine(key, oldV, newV, Changed));
            }
            else if (hasNew)
            {
                added++;
                lines.Add(new DiffLine(key, null, newV, Added));
            }
            else
            {
                removed++;
                lines.Add(new DiffLine(key, oldV, null, Removed));
            }
        }
        return new UploadDiff(lines, added, changed, removed, unchanged);
    }

    // ── 키·값 형식 (기존 스냅샷과 파일 쪽이 같은 함수를 쓴다) ──
    public static string PoKey(string soNumber, int? soLineNo) => soLineNo is int l ? $"{soNumber}-{l}" : soNumber;

    public static string PoValue(string itemNo, decimal orderQty, DateTime? due) =>
        $"품번 {itemNo} · 발주 {N(orderQty)} · 납기 {(due is DateTime d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—")}";

    public static string DateKey(string itemNo, DateOnly date) => $"{itemNo} {date:yyyy-MM-dd}";

    public static string PlanValue(decimal scheduled, decimal poQty) => $"예정 {N(scheduled)} · P/O {N(poQty)}";

    public static string DailyPoValue(decimal orderQty, decimal deliveredQty) => $"발주 {N(orderQty)} · 납품 {N(deliveredQty)}";

    static string N(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
