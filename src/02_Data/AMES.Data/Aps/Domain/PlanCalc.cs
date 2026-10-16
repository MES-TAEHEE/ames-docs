using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

/// bundle → result · loads · traces. 규칙은 docs/reference/oracle-schema.md 「계산 규칙」 (원본 응답 역산).
public static class PlanCalc
{
    static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// StageRules 의 루트(체인 기준 완제품 라인). 사출 라인 모드(스펙 §4.2)에서는 선택 라인이 사출 라인이라 루트로 넘기면
    /// 모든 사출 행이 "루트 자신"(선행 0 · 재고 사용)이 된다 — null 을 넘겨 라인 공통 행 → 유형 기본값을 완제품 모드와 같게 적용한다.
    public static string? RootLine(PlanBundle b) => b.Line.Type == "injection" ? null : b.Line.LineCd;

    /// ORIG PlanCalculator.cs:19-24 StatusOf — "warn" 은 ApsOptions.WarnStatus 일 때만 (스펙 §3.4 어휘 ok|short|warn). warn=false 면 REBUILD 3항식과 같다.
    static string StatusOf(double value, double safetyStock, bool warn) =>
        value < 0 ? "short" : warn && safetyStock > 0 && value < safetyStock ? "warn" : "ok";

    /// 사출 소요[사출행][날짜] = Σ 부모 supply(d + offset) × qtyPer. 범위 밖은 0(MD_ApsLineStage 음수 선행일의 d + offset < 0 포함).
    /// bundle 은 건드리지 않는다 — 원본 응답의 bundle.injection[].days[].requirement 는 항상 0 이고 소요는 result 에만 실린다.
    public static double[][] Requirements(PlanBundle b, StageRules stages) => RequirementsWithFolds(b, stages).req;

    /// <summary>
    /// 사출 소요 + 당김 목록. 사출일 i 의 소요 = 완제품 공급[i + 선행일] × QtyPer(원본). 선행일 때문에 사출일이 기준일 앞으로 떨어지는 공급
    /// (j &lt; 선행일 — 기준일 당일 공급 등)은 원본이 조용히 버렸지만, 그러면 기준일 공급만큼 WO 가 빠진다(실행 #1355·#555). 10-06 사용자 결정으로
    /// 수주 지연분을 첫날에 넣는 규칙과 같이 **첫 사출일(기준일)에 몬다**(StageRules.FoldPreHorizon — AMES 경로만, 골든 픽스처는 원본대로 버린다). folds 는 경고용(품번, 수량, 선행일).
    /// </summary>
    public static (double[][] req, List<(string PartNo, double Qty, int OffsetDays)> folds) RequirementsWithFolds(PlanBundle b, StageRules stages)
    {
        var supply = b.Assembly.ToDictionary(r => r.PartNo, r => r.Days.Select(d => d.Supply).ToArray(), StringComparer.OrdinalIgnoreCase);
        var parents = b.Bom.ToLookup(e => e.ChildPartNo, StringComparer.OrdinalIgnoreCase);
        var res = new double[b.Injection.Count][];
        var folds = new List<(string, double, int)>();
        var root = RootLine(b);
        for (var k = 0; k < b.Injection.Count; k++)
        {
            var inj = b.Injection[k];
            var off = stages.OffsetDays(inj.LineCd ?? "", root);
            res[k] = new double[inj.Days.Count];
            double ReqAt(int j)
            {
                double req = 0;
                if (j >= 0 && j < b.Dates.Count)
                    foreach (var e in parents[inj.PartNo])
                        if (supply.TryGetValue(e.ParentPartNo, out var sp) && j < sp.Length) req += sp[j] * e.QtyPer;
                return req;
            }
            for (var i = 0; i < inj.Days.Count; i++) res[k][i] = ReqAt(i + off);
            if (stages.FoldPreHorizon && inj.Days.Count > 0 && off > 0)
            {
                double folded = 0;
                for (var j = 0; j < off; j++) folded += ReqAt(j);   // 사출일 i = j − off < 0 — 계획판 앞
                if (folded > 0) { res[k][0] += folded; folds.Add((inj.PartNo, folded, off)); }
            }
        }
        return (res, folds);
    }

    /// result(완제품·사출 셀 · summary) 와 loads. traces(판단 근거)는 스케줄러 설명 단계가 만든다 (InjectionScheduler).
    public static (PlanResult result, List<LoadRow> loads) Compute(PlanBundle b, ShiftRules rules, StageRules stages, ApsOptions? options = null)
    {
        var warn = (options ?? ApsOptions.Default).WarnStatus;
        var req = Requirements(b, stages);
        var result = new PlanResult();
        foreach (var r in b.Assembly)
        {
            var ar = new AssemblyResult { PartNo = r.PartNo };
            var stock = r.OpeningStock + r.SisProduced - r.Shipped + r.Defect;
            foreach (var d in r.Days)
            {
                ar.Cells.Add(new AssemblyCell { Date = d.Date, Stock = stock, T = d.T, Supply = d.Supply, Demand = d.Demand, Status = StatusOf(stock, r.SafetyStock, warn) });
                stock = stock + d.Supply - d.Demand;
            }
            result.Assembly.Add(ar);
        }

        for (var k = 0; k < b.Injection.Count; k++)
        {
            var r = b.Injection[k];
            var ir = new InjectionResult { PartNo = r.PartNo };
            var stock = r.OpeningStock + r.SisProduced - r.Used + r.Defect;
            for (var i = 0; i < r.Days.Count; i++)
            {
                var d = r.Days[i]; var need = req[k][i];
                var plan = d.PlanDay + d.PlanNight;
                var remain = stock - need + plan;
                var cell = new InjectionCell
                {
                    Date = d.Date, Stock = stock, Requirement = need, PlanDay = d.PlanDay, PlanNight = d.PlanNight, PlanShifts = d.PlanShifts,
                    Remain = remain, Status = StatusOf(remain, r.SafetyStock, warn),
                };
                if (plan > 0)
                {
                    cell.Shots = Math.Ceiling(plan / Math.Max(1, r.Cavity ?? 1));
                    cell.RunHours = r.Uph > 0 ? Math.Round(plan / r.Uph, 2) : 0;
                }
                ir.Cells.Add(cell);
                stock = remain;
            }
            result.Injection.Add(ir);
        }

        var loads = BuildLoads(b, rules);
        for (var i = 0; i < b.Dates.Count; i++)
        {
            var date = b.Dates[i];
            result.Summary.Add(new SummaryRow
            {
                Date = date,
                AssemblyDemand = b.Assembly.Sum(r => r.Days[i].Demand),
                AssemblySupply = b.Assembly.Sum(r => r.Days[i].Supply),
                InjectionRequirement = req.Sum(x => x[i]),
                InjectionPlan = b.Injection.Sum(r => r.Days[i].PlanDay + r.Days[i].PlanNight),
                RunHours = Math.Round(loads.Where(l => l.Date == date).Sum(l => l.Hours), 1),
                ShortageCount = result.Assembly.Count(a => a.Cells[i].Status == "short"),
            });
        }
        return (result, loads);
    }

    /// 사출기 × 날짜 부하. 같은 금형(moldCode)의 LH/RH 는 한 번에 찍히므로 금형 단위로 한 번만 센다(수량은 형제 중 최대).
    /// 주간 = min(총시간, 주간 능력), 야간 = 나머지 — 원본 loads 는 planDay/planNight 가 아니라 총시간을 교대 능력으로 나눈다.
    public static List<LoadRow> BuildLoads(PlanBundle b, ShiftRules rules)
    {
        var loads = new List<LoadRow>();
        foreach (var line in b.Injection.Select(r => r.LineCd ?? "").Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            var groups = b.Injection.Where(r => (r.LineCd ?? "") == line && r.Uph > 0)
                .GroupBy(r => r.MoldGroupKey(), StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var date in b.Dates)
            {
                double total = 0;
                foreach (var g in groups)   // 금형별 시간을 먼저 소수 2자리로 반올림한 뒤 더한다 (픽스처 14/14 일치)
                {
                    var qty = g.Max(r => { var d = r.Days.First(x => x.Date == date); return d.PlanDay + d.PlanNight; });
                    var uph = g.Max(r => r.Uph);
                    total += Math.Round(qty / uph, 2);
                }
                var (shifts, explicitList) = rules.ShiftsFor(line, date, true);
                var cap = shifts.Sum(x => x.Hours);
                var hours = Math.Round(total, 2);
                double firstH = shifts.Count > 0 ? shifts[0].Hours : 0;
                // 교대 목록 모드: 교대별 시간 = 계획의 교대별 수량 ÷ UPH(금형 그룹마다 형제 최대 — total 과 같은 규칙, 리뷰 F2). 주/야는 파생(첫 교대 / 나머지)
                List<ShiftHours>? perShift = null;
                if (explicitList)
                {
                    perShift = shifts.Select(x => new ShiftHours(x.Code, 0)).ToList();
                    foreach (var g in groups)
                    {
                        var uph = g.Max(r => r.Uph);
                        for (var k = 0; k < shifts.Count; k++)
                        {
                            var q = g.Max(r =>
                            {
                                var d = r.Days.First(x => x.Date == date);
                                var ps = d.PlanShifts is { Count: > 0 } p ? p : ApsShiftCompat.Restore(d.PlanDay, d.PlanNight, shifts);
                                return k < ps.Count ? ps[k].Qty : 0;
                            });
                            perShift[k] = perShift[k] with { Hours = Math.Round(perShift[k].Hours + Math.Round(q / uph, 2), 2) };
                        }
                    }
                }
                var day = perShift is not null ? perShift[0].Hours : Math.Round(Math.Min(total, firstH), 2);
                var night = perShift is not null ? Math.Round(perShift.Skip(1).Sum(x => x.Hours), 2) : Math.Round(Math.Max(0, total - firstH), 2);
                loads.Add(new LoadRow
                {
                    LineCd = line, Date = date, Hours = hours, DayHours = day, NightHours = night, ShiftHours = perShift, Capacity = cap,
                    Rate = cap > 0 ? Math.Round(hours / cap * 100, 1) : 0, Over = hours > cap + 0.005,
                });
            }
        }
        return loads;
    }

}
