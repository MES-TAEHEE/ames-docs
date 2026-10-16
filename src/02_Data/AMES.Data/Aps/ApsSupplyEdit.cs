using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;

namespace AMES.Data.Aps;

/// <summary>
/// PP-APS 완제품 공급 셀 편집의 부수 규칙. 공급을 올리면 그 소요가 떨어지는 사출 칸(사출일 = 공급일 − 선행일, 기준일 앞이면 첫 사출일 —
/// PlanCalc.RequirementsWithFolds 와 같은 자리)의 잠금을 풀어 재배분이 그 칸을 늘리게 한다. 등록 계획으로 잠긴 채 두면 엔진은 그 칸을
/// 못 건드려 늘어난 소요를 첫 번째 안 잠긴 뒤 날짜로 밀고, WO 가 조립일 뒤에 나온다(실행 #507: 10-16 공급 +100 → 10-20 사출).
/// 10-15 사용자 결정 "잠금을 풀어서 자동으로 늘리게". 공급을 내릴 때는 부르지 않는다 — 이미 나간 WO 슬롯 아래로 계획만 줄이는 셈이라 잠금을 둔다.
/// </summary>
public static class ApsSupplyEdit
{
    public sealed record Unlocked(string PartNo, string Date);

    /// <param name="assyPartNo">공급을 고친 완제품 품번(BOM 부모).</param>
    /// <param name="dayIndex">고친 공급 칸의 날짜 인덱스(bundle.Dates 기준).</param>
    /// <returns>이번에 잠금을 푼 사출 칸(품번·날짜) — 이미 열려 있던 칸은 포함하지 않는다.</returns>
    public static List<Unlocked> UnlockInjectionFor(PlanBundle b, StageRules stages, string assyPartNo, int dayIndex)
    {
        var unlocked = new List<Unlocked>();
        if (dayIndex < 0 || dayIndex >= b.Dates.Count) return unlocked;
        var root = PlanCalc.RootLine(b);
        foreach (var e in b.Bom)
        {
            if (!string.Equals(e.ParentPartNo, assyPartNo, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var inj in b.Injection)
            {
                if (!string.Equals(inj.PartNo, e.ChildPartNo, StringComparison.OrdinalIgnoreCase)) continue;
                var i = dayIndex - stages.OffsetDays(inj.LineCd ?? "", root);
                if (i < 0) { if (!stages.FoldPreHorizon) continue; i = 0; }
                if (i >= inj.Days.Count) continue;
                var d = inj.Days[i];
                if (!d.Locked) continue;
                d.Locked = false;
                unlocked.Add(new Unlocked(inj.PartNo, d.Date));
            }
        }
        return unlocked;
    }
}
