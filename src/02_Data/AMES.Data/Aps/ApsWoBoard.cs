using AMES.Data.Repositories;
using AMES.Data.Scheduling;
using static AMES.Data.Repositories.PpRepository;
using ScheduleRow = AMES.Data.Repositories.LineScheduleRepository.ScheduleRow;

namespace AMES.Data.Aps;

/// <summary>
/// PP-APS 「WO 생성」 미리보기 보드(2026-10-07) — CreateApsWorkOrders 결과(dryRun 또는 실행)와 기존 PP_LineSchedule 행을
/// 날짜 탭 × 라인 줄 × 블록으로 묶는 순수 규칙. 화면(ApsWoPreview.razor)은 이 결과를 PP-LSB 와 같은 24시간 줄로 그린다.
/// - 날짜 = 대상 사출 계획일 ∪ 배치된 슬롯·MC 의 날짜(오름차순). 라인 줄은 넘긴 순서 그대로이고 비어도 나오며, 목록에 없는 라인은 끝에 붙는다.
/// - 새 블록 = 결과의 Placements(길이 > 0)·MoldChanges. 형제 0분 슬롯(StartMin == EndMin, 대표 StartMin 에 수량만 기록)은 같은 줄에서
///   그 시각을 품는 새 블록(먼저 StartMin 이 같은 것)에 Siblings 로 붙는다 — 대표가 없으면 버리지 않고 0분 블록으로 남긴다.
/// - 기존 블록 = 그 (라인, 날짜)의 PP_LineSchedule 행 중 길이 > 0 인 WO·MC·PM. 실행 뒤 다시 읽으면 결과 WO 의 슬롯과 그 MC(RefType WO · RefID)가
///   같이 오므로 WoId 로 걸러 새 블록과 겹치지 않게 한다.
/// - Shortfall 은 단계 라인 줄의 "그 WO 계획일" 탭에 둔다(DeadlinePacker 의 StepShortfall 에는 날짜가 없다).
/// </summary>
public static class ApsWoBoard
{
    public enum BlockKind { NewWo, NewMoldChange, OldWo, OldMoldChange, Pm }

    public sealed record Sibling(string WoNumber, string ItemNo, decimal Qty);

    public sealed record Block(BlockKind Kind, int StartMin, int EndMin, string? WoNumber, string? ItemNo, decimal Qty, bool Late, string? MoldId,
                               IReadOnlyList<Sibling> Siblings, string? Title = null)
    {
        public int Minutes => EndMin - StartMin;
        public bool IsNew => Kind is BlockKind.NewWo or BlockKind.NewMoldChange;
    }

    public sealed record Shortfall(string WoNumber, string ItemNo, int StepSeq, bool IsInjection, decimal Qty);

    public sealed record Row(string LineId, IReadOnlyList<Block> Blocks, IReadOnlyList<Shortfall> Shortfalls)
    {
        /// <summary>그 줄에 새로 실리는 수량(형제 포함).</summary>
        public decimal NewQty => Blocks.Where(b => b.Kind == BlockKind.NewWo).Sum(b => b.Qty + b.Siblings.Sum(s => s.Qty));
        /// <summary>그 줄에 새로 점유하는 분(새 WO + 새 MC — PP-LSB 처럼 MC 도 부하다).</summary>
        public int NewMin => Blocks.Where(b => b.IsNew).Sum(b => b.Minutes);
        public decimal ShortQty => Shortfalls.Sum(s => s.Qty);
    }

    public sealed record Day(DateOnly Date, IReadOnlyList<Row> Rows);

    /// <param name="existing">(라인, 날짜) → 기존 PP_LineSchedule 행. 화면이 LineScheduleRepository.GetSchedule 로 읽어 넘긴다.</param>
    public static IReadOnlyList<Day> Build(ApsWoResult result, IEnumerable<DateOnly> planDates, IReadOnlyList<string> lines,
                                           Func<(string Line, DateOnly Date), IReadOnlyList<ScheduleRow>> existing)
    {
        var dates = planDates
            .Concat(result.Orders.SelectMany(o => o.Placements).Select(p => DateOnly.FromDateTime(p.Date)))
            .Concat(result.Orders.SelectMany(o => o.MoldChanges).Select(m => DateOnly.FromDateTime(m.Date)))
            .Distinct().OrderBy(d => d).ToList();
        var resultWoIds = result.Orders.Select(o => o.WoId).ToHashSet();

        var days = new List<Day>(dates.Count);
        foreach (var date in dates)
        {
            var rowLines = lines.ToList();
            foreach (var extra in result.Orders.SelectMany(o => o.Placements.Select(p => (p.LineId, p.Date)).Concat(o.MoldChanges.Select(m => (m.LineId, m.Date))))
                                               .Where(x => DateOnly.FromDateTime(x.Date) == date).Select(x => x.LineId)
                                               .Concat(result.Orders.Where(o => o.PlanDate == date).SelectMany(o => o.Shortfalls).Select(s => s.LineId)))
                if (!string.IsNullOrEmpty(extra) && !rowLines.Contains(extra, StringComparer.OrdinalIgnoreCase)) rowLines.Add(extra);

            var rows = new List<Row>(rowLines.Count);
            foreach (var line in rowLines)
                rows.Add(BuildRow(line, date, result, resultWoIds, existing((line, date))));
            days.Add(new Day(date, rows));
        }
        return days;
    }

    static Row BuildRow(string line, DateOnly date, ApsWoResult result, HashSet<int> resultWoIds, IReadOnlyList<ScheduleRow> existing)
    {
        bool Here(string l, DateTime d) => string.Equals(l, line, StringComparison.OrdinalIgnoreCase) && DateOnly.FromDateTime(d) == date;

        var blocks = new List<Block>();
        var siblings = new List<(DeadlinePacker.Placement P, ApsWoOrder O)>();
        foreach (var o in result.Orders)
        {
            foreach (var p in o.Placements.Where(p => Here(p.LineId, p.Date)))
            {
                if (p.EndMin > p.StartMin)
                    blocks.Add(new Block(BlockKind.NewWo, p.StartMin, p.EndMin, o.WoNumber, o.ItemNo, p.Qty, p.Late, p.MoldId, new List<Sibling>()));
                else siblings.Add((p, o));
            }
            foreach (var m in o.MoldChanges.Where(m => Here(m.LineId, m.Date) && m.EndMin > m.StartMin))
                blocks.Add(new Block(BlockKind.NewMoldChange, m.StartMin, m.EndMin, o.WoNumber, o.ItemNo, 0, m.Late, m.ToMoldId, Array.Empty<Sibling>(),
                                     m.FromMoldId is null ? m.ToMoldId : $"{m.FromMoldId} → {m.ToMoldId}"));
        }
        // 형제 0분 슬롯 → 대표(StartMin 같음 → 그 시각을 품는 새 WO 블록) 에 붙인다. 대표가 없으면 0분 블록으로 남겨 수량이 사라지지 않게
        foreach (var (p, o) in siblings)
        {
            var rep = blocks.FirstOrDefault(b => b.Kind == BlockKind.NewWo && b.StartMin == p.StartMin)
                   ?? blocks.FirstOrDefault(b => b.Kind == BlockKind.NewWo && b.StartMin <= p.StartMin && p.StartMin < b.EndMin);
            if (rep is null) blocks.Add(new Block(BlockKind.NewWo, p.StartMin, p.EndMin, o.WoNumber, o.ItemNo, p.Qty, p.Late, p.MoldId, new List<Sibling>()));
            else ((List<Sibling>)rep.Siblings).Add(new Sibling(o.WoNumber, o.ItemNo, p.Qty));
        }

        foreach (var r in existing.Where(r => r.EndMin > r.StartMin))
        {
            switch (r.EntryType)
            {
                case "PM":
                    blocks.Add(new Block(BlockKind.Pm, r.StartMin, r.EndMin, null, null, 0, false, null, Array.Empty<Sibling>(), r.Title));
                    break;
                case "MC":
                    if (r.RefType == "WO" && r.RefId is int refId && resultWoIds.Contains(refId)) continue;
                    blocks.Add(new Block(BlockKind.OldMoldChange, r.StartMin, r.EndMin, null, null, 0, false, r.MoldId, Array.Empty<Sibling>(), r.Title ?? r.MoldId));
                    break;
                default:
                    if (r.WoId is int woId && resultWoIds.Contains(woId)) continue;
                    blocks.Add(new Block(BlockKind.OldWo, r.StartMin, r.EndMin, r.WoNumber ?? (r.WoId is int w ? $"#{w}" : null), r.ItemName, r.PlannedQty, false, r.MoldId, Array.Empty<Sibling>()));
                    break;
            }
        }

        var shorts = result.Orders.Where(o => o.PlanDate == date)
            .SelectMany(o => o.Shortfalls.Where(s => string.Equals(s.LineId, line, StringComparison.OrdinalIgnoreCase))
                              .Select(s => new Shortfall(o.WoNumber, o.ItemNo, s.StepSeq, s.StepSeq == InjStepSeq(o), s.Qty)))
            .ToList();
        return new Row(line, blocks, shorts);
    }

    /// <summary>CreateApsWorkOrders 는 조각마다 INJ 단계를 배치 또는 Shortfall 로 반드시 남기고 완제품 단계는 그보다 뒤 StepSeq 다 — 가장 작은 StepSeq 가 INJ.</summary>
    public static int InjStepSeq(ApsWoOrder o) =>
        o.Placements.Select(p => p.StepSeq).Concat(o.Shortfalls.Select(s => s.StepSeq)).DefaultIfEmpty(int.MinValue).Min();
}
