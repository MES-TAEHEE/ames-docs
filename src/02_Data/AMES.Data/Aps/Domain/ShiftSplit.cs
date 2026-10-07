using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

/// <summary>
/// 교대 가동 시간 비율 분할(스펙 §2, 2026-10-07 사용자 결정) — share_k = qty × h_k ÷ Σh. 시간이 있는 마지막 교대를 제외하고 포장 단위로 내리고,
/// 마지막 교대가 나머지를 받아 합을 보존한다. 시간 0 인 교대는 0, Σh = 0 이면 전량 첫 교대. 순수 함수 — 정본 테스트 ShiftSplitTests.
/// </summary>
public static class ShiftSplit
{
    public static List<ShiftQty> Proportional(double qty, IReadOnlyList<ShiftHours> shifts, int pack)
    {
        var result = shifts.Select(s => new ShiftQty(s.Code, 0)).ToList();
        if (result.Count == 0 || qty <= 0) return result;
        double total = shifts.Sum(s => Math.Max(0, s.Hours));
        if (total <= 1e-9) { result[0] = result[0] with { Qty = qty }; return result; }
        int last = -1;
        for (var i = shifts.Count - 1; i >= 0; i--) if (shifts[i].Hours > 0) { last = i; break; }
        double assigned = 0;
        for (var i = 0; i < shifts.Count; i++)
        {
            if (shifts[i].Hours <= 0) continue;
            double q = i == last ? Math.Max(0, qty - assigned) : Floor(qty * shifts[i].Hours / total, pack);
            result[i] = result[i] with { Qty = q };
            assigned += q;
        }
        return result;
    }

    static double Floor(double v, int pack) => pack > 1 ? Math.Floor(v / pack + 1e-9) * pack : Math.Floor(v + 1e-9);
}
