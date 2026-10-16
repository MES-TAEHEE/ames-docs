using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

/// 완제품 공급(PROD PLAN) 자동 계산. 규칙은 원본 autofill_LQ10.json 300 셀·traces 로 확정 (tools/rules-lab/autofill_lab.py 300/300):
///   cover  = CoverFor(기간 일평균 수요)
///   base   = 다음 3일 수요의 최대 (기간 끝이면 있는 만큼, 마지막 날은 0)
///   target = cover × base
///   need   = 올림_pack(demand + target − prevClosing), min = 올림_pack(demand − prevClosing)
///   cap    = AsmCapFor(행 라인, date).cap — 라인별 Σneed 가 넘으면 extra(need − min) 큰 품번부터(동률은 행 순서) 넘친 만큼 깎는다.
///            한 품번에서 깎는 양은 min(extra, 남은 초과) 이라 마지막 한 품번만 pack 배수가 아닐 수 있다.
///   시간 모드(2026-10-12 사용자 결정 — 품번마다 BOP 사이클이 다르다): 그 라인 행 전부에 CycleSec 가 있고 AsmHoursFor 가 있으면
///            능력 = 가동 시간 × 60 분, 행의 무게 w = CycleSec ÷ 60(분/EA), 초과 = Σ need × w − 능력. 깎는 순서는 같고 품번당 cut = min(extra, ceil(초과 ÷ w)) EA.
///            수량 모드는 w = 1 로 같은 식이라 골든(CycleSec 없음)은 그대로다. 정본 AsmTimeCapacityTests.
///   closing = prevClosing + supply − demand → 다음날 prevClosing. 잠긴 칸은 값 그대로 두고 능력에는 포함한다.
///   ApsOptions(PullForwardSupply · SafetyStockTerm) 는 기본 false — 켜지 않으면 위 규칙(골든)과 완전히 같다.
public static class Autofill
{
    public static List<string> Run(PlanBundle b, ShiftRules rules, StageRules stages, ApsOptions? options = null) => RunWithTraces(b, rules, stages, options).warnings;

    public static (List<string> warnings, List<AutofillTrace> traces) RunWithTraces(PlanBundle b, ShiftRules rules, StageRules stages, ApsOptions? options = null)
    {
        var opt = options ?? ApsOptions.Default;
        var warnings = new List<string>();
        var traces = new List<AutofillTrace>();
        var n = b.Dates.Count;
        var prev = b.Assembly.ToDictionary(r => r.PartNo, r => r.OpeningStock + r.SisProduced - r.Shipped + r.Defect);
        var cover = b.Assembly.ToDictionary(r => r.PartNo, r => rules.CoverFor(r.Days.Count > 0 ? r.Days.Average(d => d.Demand) : 0));
        var pack = b.Assembly.ToDictionary(r => r.PartNo, r => rules.PackFor(r.PartNo));
        var trim = rules.TrimLastDay;

        for (var i = 0; i < n; i++)
        {
            var date = b.Dates[i];
            var items = new List<Item>();
            foreach (var r in b.Assembly)
            {
                var d = r.Days[i]; var p = prev[r.PartNo];
                var last = i == n - 1;
                var future = r.Days.Skip(i + 1).Take(3).Select(x => x.Demand).ToList();
                var baseDemand = last && trim ? 0 : (future.Count > 0 ? future.Max() : 0);
                var target = cover[r.PartNo] * baseDemand;
                if (opt.SafetyStockTerm && !(last && trim)) target = Math.Max(target, r.SafetyStock);   // ORIG SupplyPlanner.cs:134 — 마지막 날 trim 이 max 보다 우선
                var need = ShiftRules.RoundUp(d.Demand + target - p, pack[r.PartNo]);
                var min = ShiftRules.RoundUp(d.Demand - p, pack[r.PartNo]);
                if (d.Locked) { need = d.Supply; min = d.Supply; }
                items.Add(new Item(r, d, need, min, baseDemand, target, p));
            }

            var supply = items.ToDictionary(x => x.Row.PartNo, x => x.Need);
            var pulled = new Dictionary<string, double>();   // 품번별 앞날로 당긴 양 — 이 날 아침 재고에 더한다 (ORIG :181 stock[row] += move)
            // 능력은 행 자기 라인 기준 — 사출 라인 모드(스펙 §4.2)는 부모 완제품 행이 여러 라인에 걸친다. 완제품 모드는 전부 선택 라인이라 그룹 1개(골든 그대로)
            foreach (var g in items.GroupBy(x => CapLine(b, x.Row), StringComparer.OrdinalIgnoreCase))
            {
                var capLine = g.Key;
                var group = g.ToList();
                var (cap, timeMode, hours) = CapOf(rules, capLine, date, group);
                var over = group.Sum(x => x.Need * Weight(x.Row, timeMode)) - cap;
                if (over <= 0) continue;
                foreach (var (x, _) in group.Select((x, idx) => (x, idx)).OrderByDescending(t => t.x.Need - t.x.Min).ThenBy(t => t.idx))
                {
                    if (over <= 0) break;
                    var w = Weight(x.Row, timeMode);
                    var cut = Math.Min(x.Need - x.Min, Math.Ceiling(over / w));
                    supply[x.Row.PartNo] -= cut; over -= cut * w;
                }
                if (over > 0 && opt.PullForwardSupply)
                    over = PullForward(b, rules, capLine, group, supply, pack, pulled, i, over, timeMode);   // ORIG SupplyPlanner.cs:163-185
                if (over > 0)
                    warnings.Add(timeMode
                        ? $"{capLine} {date} 는 라인 가동 {hours:0.#}h 로 당일 수요 최소치도 {over:0}분 담지 못했습니다."
                        : $"{capLine} {date} 는 라인 능력 {cap:0}개로 당일 수요 최소치도 {over:0}개 담지 못했습니다.");
            }

            foreach (var x in items)
            {
                var s = supply[x.Row.PartNo];
                if (!x.Day.Locked) x.Day.Supply = s;
                var p = x.Prev + pulled.GetValueOrDefault(x.Row.PartNo);   // 앞날에 만든 만큼 이 날 아침 재고가 많다 (골든 경로는 pulled 빈 사전 → x.Prev)
                var closing = p + x.Day.Supply - x.Day.Demand;
                traces.Add(new AutofillTrace
                {
                    PartNo = x.Row.PartNo, Date = date, PrevClosing = p, Demand = x.Day.Demand, DaysOfCover = cover[x.Row.PartNo],
                    TargetStock = x.Target, BaseDemand = x.BaseDemand, PackSize = pack[x.Row.PartNo], RawNeed = x.Day.Supply, Supply = x.Day.Supply, Closing = closing,
                });
                prev[x.Row.PartNo] = closing;
            }
        }
        return (warnings, traces);
    }

    /// 능력을 재는 라인 — 행 라인, 없으면 선택 라인(ApsPlanLines 와 같은 규칙).
    static string CapLine(PlanBundle b, AssemblyRow r) => string.IsNullOrEmpty(r.LineCd) ? b.Line.LineCd : r.LineCd;

    /// "-"(전체 보기) 는 능력 제한 없음. 능력을 모르는 라인(AsmCapFor = null)도 제한 없음.
    /// 시간 모드(그룹 행 전부 CycleSec > 0 · 라인 가동 시간 있음)는 능력이 분, 아니면 수량.
    static (double cap, bool timeMode, double hours) CapOf(ShiftRules rules, string capLine, string date, IReadOnlyList<Item> group)
    {
        if (capLine == "-") return (double.PositiveInfinity, false, 0);
        if (group.Count > 0 && group.All(x => x.Row.CycleSec is > 0) && rules.AsmHoursFor(capLine, date) is double h)
            return (h * 60, true, h);
        return (rules.AsmCapFor(capLine, date)?.cap ?? double.PositiveInfinity, false, 0);
    }

    /// 행 1 EA 가 먹는 능력 — 시간 모드는 분(CycleSec ÷ 60), 수량 모드는 1.
    static double Weight(AssemblyRow r, bool timeMode) => timeMode ? r.CycleSec!.Value / 60 : 1;

    /// ORIG SupplyPlanner.cs:163-185 — 남은 초과분(over)을 공급 큰 품번부터 앞 근무일(i−1 → 0)의 남은 능력으로 옮긴다.
    /// 앞날 잠긴 칸은 받지 않고, 옮기는 양은 pack 배수로 내림, 앞날 Day.Supply 에 직접 더한다(그 날 트레이스는 다시 쓰지 않는다 — 시트 §9.1).
    /// 오늘 잠긴 행은 :63 이 supply 를 쓰지 않으므로 옮기지 않는다(ORIG 와 다른 점: 옮기면 이중 계상). 남은 over 를 돌려준다.
    /// group = capLine 에 속한 행 전부 — 옮길 품번도, 앞날 여유(room)도 그 라인 행만 센다.
    /// 시간 모드는 over·room 이 분이고 옮기는 양은 EA(÷ 무게) — 앞날 여유도 그 날의 가동 분에서 그 라인 행의 공급 × 무게를 뺀다.
    static double PullForward(PlanBundle b, ShiftRules rules, string capLine, List<Item> group, Dictionary<string, double> supply, Dictionary<string, int> pack,
                              Dictionary<string, double> pulled, int i, double over, bool timeMode)
    {
        foreach (var (x, _) in group.Select((x, idx) => (x, idx)).OrderByDescending(t => supply[t.x.Row.PartNo]).ThenBy(t => t.idx))
        {
            if (over <= 0) break;
            if (x.Day.Locked) continue;
            var partNo = x.Row.PartNo; var pk = pack[partNo]; var w = Weight(x.Row, timeMode);
            for (var j = i - 1; j >= 0 && over > 0 && supply[partNo] > 0; j--)
            {
                var earlier = x.Row.Days[j];
                if (earlier.Locked) continue;
                var (capJ, modeJ, _) = CapOf(rules, capLine, b.Dates[j], group);
                if (modeJ != timeMode) continue;   // 앞날 능력 단위가 다르면(패턴 없음 등) 비교할 수 없어 당기지 않는다
                var room = capJ - group.Sum(r => r.Row.Days[j].Supply * Weight(r.Row, timeMode));   // used[j] = 그 라인에 그 날 이미 잡힌 공급(잠긴 칸 포함)
                if (room <= 0) continue;
                var move = Math.Min(Math.Min(room, over) / w, supply[partNo]);
                move = pk > 1 ? Math.Floor(move / pk) * pk : Math.Floor(move);
                if (move <= 0) continue;
                earlier.Supply += move;
                pulled[partNo] = pulled.GetValueOrDefault(partNo) + move;
                supply[partNo] -= move;
                over -= move * w;
            }
        }
        return over;
    }

    sealed record Item(AssemblyRow Row, AssemblyDay Day, double Need, double Min, double BaseDemand, double Target, double Prev);
}
