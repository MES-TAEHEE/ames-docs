using AMES.Data.Scheduling;
using static AMES.Data.Repositories.PpRepository;

namespace AMES.Data.Aps;

/// <summary>
/// PP-APS WO 미리보기 보드에서 옮긴 슬롯 하나(2026-10-07 사용자 결정 A — 생성 전에 편집하고 「WO 생성」때 반영).
/// 정체는 잠정 WO 번호가 아니라 (품번, 계획일, 수주, 단계, 원래 라인·날짜·시작·끝) — 실제 실행에서 번호가 달라져도 같은 슬롯을 찾는다.
/// NewQty 는 슬롯 수량만 바꾸고 WO 수량은 건드리지 않는다(null = 그대로).
/// </summary>
public sealed record ApsSlotEdit(string ItemNo, DateOnly PlanDate, int? SoId, int StepSeq,
                                 string LineId, DateOnly Date, int StartMin, int EndMin,
                                 string NewLineId, DateOnly NewDate, int NewStartMin, int NewEndMin, decimal? NewQty)
{
    public SlotPacker.Interval NewInterval => new(NewStartMin, NewEndMin);
    public bool SameDay => string.Equals(LineId, NewLineId, StringComparison.OrdinalIgnoreCase) && Date == NewDate;
    public bool LineChanged => !string.Equals(LineId, NewLineId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>편집 하나의 결과 — Applied 가 false 면 Reason(ApsSlotEditRules.Reason*).</summary>
public sealed record ApsSlotEditOutcome(ApsSlotEdit Edit, bool Applied, string? Reason);

/// <summary>
/// 슬롯 편집 규칙(순수) — 화면(즉시 검증·보드 재그리기)과 PpRepository.CreateApsWorkOrders(DB 반영) 가 같은 규칙을 쓴다.
/// 검증은 호출자가 넘기는 delegate 가 한다(화면은 보드의 가동 밴드·다른 블록, 리포지토리는 DayStateCache·MD_MoldLine) — Apply 는 짝 맞추기·덮기만.
/// </summary>
public static class ApsSlotEditRules
{
    public const int SnapMin = 5;
    public const string ReasonNotFound = "NotFound", ReasonBadRange = "BadRange", ReasonNoPattern = "NoPattern",
                        ReasonOutsideBands = "OutsideBands", ReasonOverlap = "Overlap", ReasonNoUph = "NoUph";

    /// <summary>대표 슬롯을 옮긴 뒤 같이 옮긴 행(형제 0분 슬롯 포함) — 리포지토리가 PP_LineSchedule 행을 고치는 단위.</summary>
    public sealed record SlotMove(int WoId, int StepSeq, DeadlinePacker.Placement Old, DeadlinePacker.Placement New);

    /// <summary>검증 delegate: 편집, 지금까지 적용된 결과, 찾은 원래 슬롯 → 거부 사유(null = 통과).</summary>
    public delegate string? Validator(ApsSlotEdit edit, IReadOnlyList<ApsWoOrder> current, DeadlinePacker.Placement old);

    public static int Snap(int min) => (int)Math.Round(min / (double)SnapMin, MidpointRounding.AwayFromZero) * SnapMin;

    /// <summary>구간이 가동 밴드 하나 안에 들고(휴게를 가로지르지 않고) 점유 구간과 겹치지 않는지 — PP-LSB InsideOperating 과 같은 규칙, 하루 분 0..1440.</summary>
    public static string? Validate(SlotPacker.Interval slot, IReadOnlyList<SlotPacker.Interval> operatingBands, IEnumerable<SlotPacker.Interval> occupied)
    {
        if (slot.EndMin <= slot.StartMin || slot.StartMin < 0 || slot.EndMin > 1440) return ReasonBadRange;
        if (operatingBands.Count == 0) return ReasonNoPattern;
        if (!operatingBands.Any(b => b.StartMin <= slot.StartMin && slot.EndMin <= b.EndMin)) return ReasonOutsideBands;
        if (occupied.Any(o => o.EndMin > o.StartMin && o.StartMin < slot.EndMin && slot.StartMin < o.EndMin)) return ReasonOverlap;
        return null;
    }

    /// <summary>
    /// 편집을 순서대로 결과에 덮는다 — 원본은 바꾸지 않고 새 목록을 돌려준다. 슬롯은 (품번·계획일·수주) 의 WO 안에서 (단계·라인·날짜·시작·끝) 으로 찾고,
    /// 못 찾으면 NotFound(미리보기 뒤 배치가 달라진 슬롯). 길이 > 0 인 대표 슬롯을 옮기면 같은 라인·날짜·시작의 형제 0분 슬롯(다른 WO)도 따라간다.
    /// </summary>
    public static (List<ApsWoOrder> Orders, List<ApsSlotEditOutcome> Outcomes, List<SlotMove> Moves)
        Apply(IReadOnlyList<ApsWoOrder> orders, IEnumerable<ApsSlotEdit> edits, Validator validate)
    {
        var current  = orders.ToList();
        var outcomes = new List<ApsSlotEditOutcome>();
        var moves    = new List<SlotMove>();
        foreach (var e in edits)
        {
            var hit = Find(current, e);
            if (hit is null) { outcomes.Add(new(e, false, ReasonNotFound)); continue; }
            var (oi, pi) = hit.Value;
            var old = current[oi].Placements[pi];
            if (e.NewEndMin <= e.NewStartMin || (e.NewQty is { } q0 && q0 <= 0)) { outcomes.Add(new(e, false, ReasonBadRange)); continue; }
            if (validate(e, current, old) is { } reason) { outcomes.Add(new(e, false, reason)); continue; }

            var moved = old with { LineId = e.NewLineId, Date = e.NewDate.ToDateTime(TimeOnly.MinValue), StartMin = e.NewStartMin, EndMin = e.NewEndMin, Qty = e.NewQty ?? old.Qty };
            current[oi] = Replace(current[oi], pi, moved);
            moves.Add(new SlotMove(current[oi].WoId, old.StepSeq, old, moved));

            if (old.EndMin > old.StartMin)
                for (int j = 0; j < current.Count; j++)
                {
                    if (j == oi) continue;
                    var o = current[j];
                    for (int k = 0; k < o.Placements.Count; k++)
                    {
                        var p = o.Placements[k];
                        if (p.StepSeq != old.StepSeq || p.EndMin != p.StartMin || p.StartMin != old.StartMin
                            || !string.Equals(p.LineId, old.LineId, StringComparison.OrdinalIgnoreCase) || p.Date.Date != old.Date.Date) continue;
                        var sib = p with { LineId = moved.LineId, Date = moved.Date, StartMin = moved.StartMin, EndMin = moved.StartMin };
                        current[j] = o = Replace(o, k, sib);
                        moves.Add(new SlotMove(o.WoId, p.StepSeq, p, sib));
                    }
                }
            outcomes.Add(new(e, true, null));
        }
        return (current, outcomes, moves);
    }

    static (int OrderIdx, int PlacementIdx)? Find(List<ApsWoOrder> current, ApsSlotEdit e)
    {
        for (int i = 0; i < current.Count; i++)
        {
            var o = current[i];
            if (!string.Equals(o.ItemNo, e.ItemNo, StringComparison.OrdinalIgnoreCase) || o.PlanDate != e.PlanDate || o.SoId != e.SoId) continue;
            for (int k = 0; k < o.Placements.Count; k++)
            {
                var p = o.Placements[k];
                if (p.StepSeq == e.StepSeq && string.Equals(p.LineId, e.LineId, StringComparison.OrdinalIgnoreCase)
                    && DateOnly.FromDateTime(p.Date) == e.Date && p.StartMin == e.StartMin && p.EndMin == e.EndMin)
                    return (i, k);
            }
        }
        return null;
    }

    static ApsWoOrder Replace(ApsWoOrder o, int idx, DeadlinePacker.Placement p)
    {
        var list = o.Placements.ToList();
        list[idx] = p;
        return o with { Placements = list };
    }
}
