using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using AMES.Data.Repositories;

namespace AMES.Data.Aps;

/// <summary>
/// PlanBundle + PlanResult → PP_ApsPlanLine 정규화 사본(스펙 §3.4·§7). JSON 3개가 재현의 정본이고 이 행은 조회·WO 연결용이다.
/// ASM 행 = AssemblyRow × Days(Demand·Supply·Locked, Stock/Status 는 result.Assembly 셀), INJ 행 = InjectionRow × Days(PlanDay·PlanNight·Locked,
/// Requirement·Stock/Status 는 result.Injection 셀). 셀이 없으면 Stock 0·Status "ok". PlanLineId = RunId = 0, WoId = null. 순수 함수 — 정본 테스트 ApsPlanLinesTests.
/// </summary>
public static class ApsPlanLines
{
    public static List<ApsRepository.ApsPlanLineRow> From(PlanBundle bundle, PlanResult result)
    {
        var asmCells = result.Assembly
            .GroupBy(r => r.PartNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Cells.GroupBy(c => c.Date, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal),
                          StringComparer.OrdinalIgnoreCase);
        var injCells = result.Injection
            .GroupBy(r => r.PartNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Cells.GroupBy(c => c.Date, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal),
                          StringComparer.OrdinalIgnoreCase);

        var rows = new List<ApsRepository.ApsPlanLineRow>();
        foreach (var r in bundle.Assembly)
        {
            var line = string.IsNullOrEmpty(r.LineCd) ? bundle.Line.LineCd : r.LineCd;
            asmCells.TryGetValue(r.PartNo, out var cells);
            foreach (var d in r.Days)
            {
                AssemblyCell? c = cells is not null && cells.TryGetValue(d.Date, out var cc) ? cc : null;
                rows.Add(new(0, 0, ApsRepository.KindAsm, r.PartNo, line, ApsCalendar.Parse(d.Date),
                             Q(d.Demand), Q(d.Supply), 0m, 0m, 0m, Q(c?.Stock ?? 0), d.Locked, c?.Status ?? "ok", null));
            }
        }
        foreach (var r in bundle.Injection)
        {
            var line = string.IsNullOrEmpty(r.LineCd) ? bundle.Line.LineCd : r.LineCd;
            // 같은 품번 규칙(스펙 §4.2 ①) = 자기 간선 BomEdge(X, X, 1) → 「WO 생성」 대상. BOM 규칙 행(부모 ≠ 자식)은 false
            var sameItem = bundle.Bom.Any(e => string.Equals(e.ParentPartNo, r.PartNo, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(e.ChildPartNo,  r.PartNo, StringComparison.OrdinalIgnoreCase));
            injCells.TryGetValue(r.PartNo, out var cells);
            foreach (var d in r.Days)
            {
                InjectionCell? c = cells is not null && cells.TryGetValue(d.Date, out var cc) ? cc : null;
                rows.Add(new(0, 0, ApsRepository.KindInj, r.PartNo, line, ApsCalendar.Parse(d.Date),
                             0m, 0m, Q(c?.Requirement ?? 0), Q(d.PlanDay), Q(d.PlanNight), Q(c?.Stock ?? 0), d.Locked, c?.Status ?? "ok", null, sameItem,
                             d.PlanShifts?.Select(s => new ShiftQty(s.Code, (double)Q(s.Qty))).ToList()));
            }
        }
        return rows;
    }

    static decimal Q(double v) => Math.Round((decimal)v, 3, MidpointRounding.AwayFromZero);
}
