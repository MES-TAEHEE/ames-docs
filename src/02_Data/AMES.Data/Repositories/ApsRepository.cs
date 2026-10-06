using System.Data;
using System.Globalization;
using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using AMES.Data.Connection;
using AMES.Data.Scheduling;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// APS 어댑터 — AMES 마스터·수주·실적·슬롯을 PlanBundle 로 바꾸고(BuildBundle, 스펙 §4·§5) 실행 결과를 저장·조회한다(§7).
/// raw SqlCommand, connection-per-method. 계산 규칙은 AMES.Data.Aps 의 순수 클래스(DemandRules·ActualsRules·ShiftBands·ApsSettingsLoader)에 있고
/// 여기서는 읽기·조립만 한다. 결손은 전부 Warnings 로 올린다(조용히 0 금지) — 예외는 DB 접속·컬럼 누락뿐.
/// </summary>
public sealed class ApsRepository
{
    private readonly AmesConnectionFactory _f;
    public ApsRepository(AmesConnectionFactory f) => _f = f;

    public const string StatusSaved = "Saved", StatusReleased = "Released";
    public const string KindAsm = "ASM", KindInj = "INJ";
    /// <summary>LineInfo.Type 계약(StageRules.IsInjection 이 "injection" 을 본다).</summary>
    public const string LineTypeInjection = "injection", LineTypeAssembly = "assembly";
    /// <summary>전체 라인 모드의 LineId — 완제품 라인을 걸러내지 않고 모든 완제품 라인의 행을 한 실행에 담는다(도너 엔진의 전체 보기 표식과 같은 값, PP_ApsRun.LineID 에 그대로 저장).</summary>
    public const string AllLines = "-";
    public const string SourceAmes = "ames";

    // ── 라인 목록 ────────────────────────────────────────────────────────
    public sealed record LineChoice(string LineId, string? LineName, string Type);

    /// <summary>활성 INJ/IMG/PNT 라인(LineID Ordinal 순). INJ → injection, IMG·PNT → assembly.</summary>
    public List<LineChoice> ListLines()
    {
        using var conn = _f.OpenConnection();
        return ReadLines(conn, null).Select(l => new LineChoice(l.LineId, l.LineName, TypeOf(l.ProcessCode))).ToList();
    }

    static string TypeOf(string processCode) => MoldResolver.NeedsMold(processCode) ? LineTypeInjection : LineTypeAssembly;

    // ── 내부 행 ─────────────────────────────────────────────────────────
    sealed record LineRow(string LineId, string? LineName, string ProcessCode);
    /// <param name="InjFlag">MD_Item.InjFlag — 사출품 여부. BOM 규칙은 이 값으로 사출품을 가른다(사용자 결정 10-05): 1 이면 거기서 멈추고(금형 필요), 0 이면 서브조립로 보고 BOM 을 더 내려간다.</param>
    sealed record ItemRow(string ItemNo, string? ItemName, string? ItemType, bool InjFlag, string? RoutingType, decimal SafetyStock, string? Pgn, string? Alc, string? CarType, int? BoxQty);
    /// <param name="Color">MD_MoldItem.Color — 형제(동시 취출)·캐비티 분할의 단위는 금형 × 색상이다(같은 금형의 다른 색상은 따로 찍는다).</param>
    sealed record MoldItemRow(string MoldId, string ItemNo, string? Color, int? MoldCavity, int? MoldChangeMin);
    sealed record MoldLineRow(string LineCode, string MoldId, decimal? Uph, decimal? PrepTime);
    sealed record BomRow(string Parent, string Child, decimal QtyPer, decimal ScrapPct);
    sealed record SlotRow(string LineId, DateOnly Date, string ItemNo, int StartMin, decimal Qty, int WoId, string WoNumber);
    /// <summary>완제품 후보 — 완제품(마지막 라인 단계) 공정·라인, INJ 단계 유무, INJ 다음 라인 단계(같은 품번 사출품의 "다음 단계 실적").</summary>
    sealed record FgInfo(ItemRow Item, string FgLine, string FgProcess, bool HasInjStep, string? NextProcess, string? NextLine);
    sealed record InjInfo(string ItemNo, string LineId, string MoldId, MoldItemRow Mold, MoldLineRow? MoldLine);
    sealed record LineDay(ShiftBands.DayNightBands Full, double DayH, double NightH);

    static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static string N(double v)  => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string N(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Iso(DateOnly d) => ApsCalendar.Iso(d);
    static DateTime Dt(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);
    static string? LineOf(WorkOrderRepository.RoutingStepPreview t) =>
        t.BopLineId ?? (t.Candidates.Count == 1 ? t.Candidates[0].LineId : null);

    // ── BuildBundle (스펙 §4.2~4.5) ─────────────────────────────────────
    /// <summary>
    /// 선택 라인·기준일·일수로 PlanBundle + Settings + D-1 실적을 만든다. 읽기 전용 트랜잭션 하나로 일관된 스냅샷을 읽는다.
    /// 기준일이 DbClock.Today 가 아니면 "재고는 현재값 기준" 경고. 라인이 APS 대상이 아니면 빈 번들 + 경고.
    /// </summary>
    public ApsBuild BuildBundle(ApsQuery q)
    {
        var warnings = new List<string>();
        var today = DateOnly.FromDateTime(DbClock.Today);
        if (q.BaseDate != today)
            warnings.Add($"기준일 {Iso(q.BaseDate)} 이 오늘({Iso(today)})과 달라 재고는 현재값 기준입니다.");
        int days = Math.Clamp(q.Days, 1, 60);
        var customerId = string.IsNullOrWhiteSpace(q.CustomerId) ? null : q.CustomerId.Trim();

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            // ── 달력·날짜 ──
            var baseDt = Dt(q.BaseDate);
            var cal = new WorkdayCalendarAdapter(new WorkdayCalendar(
                PpRepository.ReadCalendar(conn, tx, baseDt.AddDays(-14), baseDt.AddDays(days * 2 + 14))));
            var dates    = ApsCalendar.WorkDates(cal, Iso(q.BaseDate), days);
            var prevIso  = ApsCalendar.PrevWorkDate(cal, dates[0]);
            var prev     = ApsCalendar.Parse(prevIso);
            var lastDate = ApsCalendar.Parse(dates[^1]);
            // 마지막 날 뒤 휴무일 납기는 마지막 날로 접히므로(§5 ③) 다음 근무일 전날까지 읽는다
            var orderTo = lastDate.AddDays(1);
            while (!cal.IsWorkday(orderTo)) orderTo = orderTo.AddDays(1);
            orderTo = orderTo.AddDays(-1);

            // ── 설정·라인 ──
            var parsed = ReadSettings(conn, tx);
            warnings.AddRange(parsed.Warnings);
            var settings = parsed.Settings;
            var lines = ReadLines(conn, tx).ToDictionary(l => l.LineId,
                l => new LineInfo { LineCd = l.LineId, LineName = l.LineName, Type = TypeOf(l.ProcessCode), Uph = 0, Active = true },
                StringComparer.OrdinalIgnoreCase);
            var dailyCap = new Dictionary<string, int?>(PpRepository.ReadDailyCap(conn, tx), StringComparer.OrdinalIgnoreCase);

            var bundle  = new PlanBundle { BaseDate = Iso(q.BaseDate), PrevDate = prevIso, Dates = dates, Source = SourceAmes, Warnings = warnings };
            var actuals = new List<ApsActualInfo>();

            bool allLines = q.LineId == AllLines;
            LineInfo selected;
            if (allLines)
                selected = new LineInfo { LineCd = AllLines, LineName = "전체", Type = LineTypeAssembly, Uph = 0, Active = true };
            else if (!lines.TryGetValue(q.LineId, out selected!))
            {
                warnings.Add($"라인 {q.LineId} 은 활성 INJ/IMG/PNT 라인이 아니라 계획을 만들 수 없습니다.");
                bundle.Line = new LineInfo { LineCd = q.LineId, Type = LineTypeAssembly };
                lines[q.LineId] = bundle.Line;
                tx.Commit();
                return Finish(bundle, settings, parsed.Options, actuals, warnings, cal, lines);
            }
            bundle.Line = selected;
            bool injectionMode = selected.Type == LineTypeInjection;

            // ── 수요 · 등록 계획 · 완제품 현재 재고 → 후보 품번 ──
            var items  = ReadItems(conn, tx);
            var plans  = q.IncludeDailyPlan ? ReadDemandPlan(conn, tx, q.BaseDate, orderTo) : (IReadOnlyList<DemandRules.PlanRow>)Array.Empty<DemandRules.PlanRow>();
            var demand = DemandRules.Build(ReadOrders(conn, tx, orderTo), plans, q.BaseDate, dates, cal, q.IncludeOpen, customerId,
                                           new HashSet<string>(items.Keys, StringComparer.OrdinalIgnoreCase));
            warnings.AddRange(demand.Warnings);
            var slots   = ReadRegisteredSlots(conn, tx, q.BaseDate, lastDate);
            var fgStock = ReadFgStock(conn, tx);

            var candidates = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            candidates.UnionWith(demand.Demand.Keys);
            candidates.UnionWith(slots.Select(s => s.ItemNo));
            candidates.UnionWith(fgStock.Where(kv => kv.Value > 0).Select(kv => kv.Key));

            // ── 완제품 후보: 라우팅 템플릿 → 라인이 있는 마지막 단계 = 완제품 공정·라인 ──
            var fg = new Dictionary<string, FgInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var itemNo in candidates)
            {
                if (!items.TryGetValue(itemNo, out var it)) continue;   // 수요 품번은 DemandRules 가 이미 경고했다(재고·슬롯만 있는 비활성 품번은 계획 대상이 아니다)
                if (Eq(it.ItemType, nameof(AMES.Contracts.Enums.ItemType.MATERIAL))) continue;   // 구매 자재(나사·하네스 등)는 생산 대상이 아니다 — 재고가 있어도 조용히 뺀다(사용자 결정 10-02)
                if (it.RoutingType is null)
                {
                    // 스펙 §4.2 — RoutingType NULL 은 후보가 된 이유(수요·재고·등록 계획)와 무관하게 제외 + 경고(§9: 조용히 빼지 않는다)
                    demand.Demand.TryGetValue(itemNo, out var dq);
                    var why = dq is not null ? $"수요 {N(dq.Sum())}개"
                            : fgStock.GetValueOrDefault(itemNo) > 0 ? $"재고 {N(fgStock[itemNo])}"
                            : "등록 계획 있음";
                    warnings.Add($"품번 {itemNo}: RoutingType 이 없어 계획에서 제외했습니다 ({why}).");
                    continue;
                }
                var tpl = WorkOrderRepository.ReadPreview(conn, tx, itemNo, it.RoutingType);
                var lineSteps = tpl.Where(t => t.LineRequired).OrderBy(t => t.StepSeq).ToList();
                if (lineSteps.Count == 0)
                {
                    warnings.Add($"품번 {itemNo}: 라우팅 {it.RoutingType} 에 라인이 있는 단계가 없어 제외했습니다.");
                    continue;
                }
                var last   = lineSteps[^1];
                var fgLine = LineOf(last);
                if (fgLine is null)
                {
                    warnings.Add($"품번 {itemNo}: 완제품 공정 {last.ProcessCode} 의 라인이 배정되지 않았습니다(BOP 스테이션 없음, 후보 {last.Candidates.Count}개) — 제외.");
                    continue;
                }
                if (!lines.ContainsKey(fgLine))
                {
                    warnings.Add($"품번 {itemNo}: 마지막 라인 단계 {last.ProcessCode}({fgLine}) 가 APS 대상 공정(INJ/IMG/PNT)이 아니라 제외했습니다.");
                    continue;
                }
                var injStep = tpl.FirstOrDefault(t => MoldResolver.NeedsMold(t.ProcessCode));
                var next    = injStep is null ? null : lineSteps.FirstOrDefault(t => t.StepSeq > injStep.StepSeq);
                fg[itemNo]  = new FgInfo(it, fgLine, last.ProcessCode, injStep is not null, next?.ProcessCode, next is null ? null : LineOf(next));
            }
            // 완제품 라인 모드: 다른 라인의 완제품은 조용히 뺀다(그 라인의 계획이다). 전체 라인 모드는 걸러내지 않는다
            if (!injectionMode && !allLines)
                foreach (var k in fg.Where(kv => !Eq(kv.Value.FgLine, q.LineId)).Select(kv => kv.Key).ToList())
                    fg.Remove(k);

            // ── 사출 행 후보: ① 같은 품번 규칙 ② BOM 규칙 → 사출 라인·금형 ──
            var moldItems       = ReadMoldItems(conn, tx);
            var moldsByItem     = moldItems.GroupBy(m => m.ItemNo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var moldsById       = moldItems.GroupBy(m => m.MoldId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var moldLines       = ReadMoldLines(conn, tx);
            var moldLineByKey   = moldLines.ToDictionary(m => (m.LineCode.ToUpperInvariant(), m.MoldId.ToUpperInvariant()));
            var moldLinesByMold = moldLines.GroupBy(m => m.MoldId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var bopInj          = ReadBopInjLines(conn, tx);
            var bom             = ReadBom(conn, tx, q.BaseDate);

            var edges  = new List<BomEdge>();
            var inj    = new Dictionary<string, InjInfo>(StringComparer.OrdinalIgnoreCase);
            var noLine = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var injWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // InjFlag·금형 불일치 경고는 품번당 1회
            foreach (var (itemNo, f) in fg)
            {
                var children = new List<(string Child, double QtyPer)>();
                if (f.HasInjStep && moldsByItem.ContainsKey(itemNo)) children.Add((itemNo, 1));
                // BOM 규칙은 다단계(사용자 결정 10-05): 마스터 리스트는 완제품 → 서브조립(S-품번·PNL ASSY·MODULE) → 코어·레일 구조라 사출품(MD_Item.InjFlag = 1)에서 멈추고,
                // InjFlag 0 인 자식은 서브조립로 보고 그 BOM 을 한 단계 더 내려가며 QtyPer(스크랩 포함)를 경로를 따라 곱한다. MATERIAL 은 내려가지 않는다.
                // 사출품인데 활성 금형이 없으면 사출 행 없음 + 경고, 금형은 있는데 InjFlag 0 이면 사출품으로 보지 않고(서브조립) 경고 — 둘 다 품번당 1회
                var found = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var order = new List<string>();
                var path  = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { itemNo };
                bool WalkBom(string parent, double mult, int depth)
                {
                    bool any = false;
                    if (!bom.TryGetValue(parent, out var comps)) return false;
                    foreach (var c in comps)
                    {
                        if (path.Contains(c.Child)) continue;   // 자기 자신·순환
                        double q = mult * EdgeQtyPer(c.QtyPer, c.ScrapPct);
                        if (!items.TryGetValue(c.Child, out var ci)) continue;   // 비활성·미등록 품번
                        bool hasMold = moldsByItem.ContainsKey(c.Child);
                        if (ci.InjFlag)
                        {
                            if (hasMold)
                            {
                                if (!found.ContainsKey(c.Child)) order.Add(c.Child);
                                found[c.Child] = found.GetValueOrDefault(c.Child) + q;   // 두 경로로 닿으면 합산
                                any = true;
                            }
                            else if (injWarned.Add(c.Child) && !injectionMode)
                                warnings.Add($"사출품 {c.Child}(InjFlag): 활성 금형이 없어 사출 행을 만들 수 없습니다 — MD-007 금형 품번 등록 필요.");
                            continue;
                        }
                        if (hasMold && injWarned.Add(c.Child) && !injectionMode)
                            warnings.Add($"품번 {c.Child}: 활성 금형은 있지만 InjFlag 가 0 이라 사출품으로 보지 않습니다(MD-003 에서 사출품 여부 확인).");
                        if (depth >= BomWalkMaxDepth || Eq(ci.ItemType, nameof(AMES.Contracts.Enums.ItemType.MATERIAL))) continue;
                        path.Add(c.Child);
                        if (WalkBom(c.Child, q, depth + 1)) any = true;
                        path.Remove(c.Child);
                    }
                    return any;
                }
                WalkBom(itemNo, 1, 0);
                foreach (var leaf in order) children.Add((leaf, found[leaf]));
                if (children.Count == 0)
                {
                    if (!injectionMode)
                        warnings.Add($"품번 {itemNo}: 사출 단계·활성 금형이 없고 BOM(다단계) 안에 금형 있는 사출품(InjFlag)도 없어 사출 행이 없습니다.");
                    continue;
                }
                foreach (var (child, qtyPer) in children)
                {
                    if (noLine.Contains(child)) continue;
                    if (!inj.ContainsKey(child))
                    {
                        var resolved = ResolveInjection(child, moldsByItem[child], moldLineByKey, moldLinesByMold, bopInj);
                        if (resolved is null)
                        {
                            noLine.Add(child);
                            if (!injectionMode)
                                warnings.Add($"사출품 {child}: 사출 라인을 정할 수 없습니다(BOP INJ 스테이션·MD_MoldLine 배정 없음) — 완제품 {itemNo} 의 사출 행 없음.");
                            continue;
                        }
                        inj[child] = resolved;
                    }
                    edges.Add(new BomEdge(itemNo, child, qtyPer));
                }
            }
            // 사출 라인 모드: 선택 라인의 사출 행 + 그 부모 완제품 행만
            if (injectionMode)
            {
                foreach (var k in inj.Where(kv => !Eq(kv.Value.LineId, q.LineId)).Select(kv => kv.Key).ToList()) inj.Remove(k);
                edges = edges.Where(e => inj.ContainsKey(e.ChildPartNo)).ToList();
                var parents = new HashSet<string>(edges.Select(e => e.ParentPartNo), StringComparer.OrdinalIgnoreCase);
                foreach (var k in fg.Keys.Where(k => !parents.Contains(k)).ToList()) fg.Remove(k);
            }

            // ── 재고·실적 (스펙 §4.4) ──
            var fgItems  = fg.Keys.ToList();
            var injItems = inj.Keys.ToList();
            var allItems = fgItems.Union(injItems, StringComparer.OrdinalIgnoreCase).ToList();
            var produced = ReadProduced(conn, tx, allItems, prev, q.BaseDate);
            var shipped  = ReadShipped(conn, tx, fgItems, prev, q.BaseDate);

            decimal Prod(string item, string proc, string? line, DateOnly d) =>
                produced.Where(kv => Eq(kv.Key.Item, item) && Eq(kv.Key.Proc, proc) && (line is null || Eq(kv.Key.Line, line)) && kv.Key.Date == d)
                        .Sum(kv => kv.Value);

            // 완제품 품번으로 잡힌 사출 라인 슬롯(코어 품번 WO 모델 — WO 는 완제품 품번, INJ 단계 슬롯도 그 품번)은 그 완제품의 BOM 사출 자식 등록 계획이다:
            // 자식의 사출 라인에 있는 부모 슬롯 수량 × 간선 QtyPer 를 자식마다 더한다(사용자 결정 10-02). 부모가 자기 금형으로 직접 찍히는 품번이면 그 슬롯은 부모 자신의 것이다
            IEnumerable<(SlotRow Slot, double QtyPer)> ParentSlotsFor(string child, string lineId) =>
                edges.Where(e => Eq(e.ChildPartNo, child) && !Eq(e.ParentPartNo, child) && !inj.ContainsKey(e.ParentPartNo))
                     .SelectMany(e => slots.Where(s => Eq(s.ItemNo, e.ParentPartNo) && Eq(s.LineId, lineId)).Select(s => (s, e.QtyPer)));

            // 등록 계획(§4.5)은 완제품 라인 슬롯과 그 사출품의 사출 라인 슬롯(자식 품번 또는 부모 품번으로 잡힌 것) 둘 다 — 사출 슬롯만 있는 품번도 행을 남겨야 그 사출기 능력이 이중으로 쓰이지 않는다
            bool HasRegisteredPlan(string itemNo, FgInfo f) =>
                slots.Any(s => Eq(s.ItemNo, itemNo) && Eq(s.LineId, f.FgLine))
                || edges.Any(e => Eq(e.ParentPartNo, itemNo) && slots.Any(s => Eq(s.ItemNo, e.ChildPartNo) && Eq(s.LineId, inj[e.ChildPartNo].LineId)))
                || edges.Any(e => Eq(e.ParentPartNo, itemNo) && ParentSlotsFor(e.ChildPartNo, inj[e.ChildPartNo].LineId).Any());

            // ── 완제품 행 ──
            // Actuals 순서 계약: 같은 품번이 완제품·사출 행에 모두 있으면 완제품 항목이 먼저, 사출 항목이 뒤(화면이 First/Last 로 가른다)
            var asmRows = new List<AssemblyRow>();
            var regWos  = new List<ApsRegisteredWo>();   // 셀의 "WO" 줄 — 등록 계획을 만든 WO(슬롯 단위)
            foreach (var (itemNo, f) in fg.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var snap = new ActualsRules.Snapshot(itemNo,
                    CurrentStock:  fgStock.GetValueOrDefault(itemNo),
                    TodayProduced: Prod(itemNo, f.FgProcess, f.FgLine, q.BaseDate),
                    TodayShipped:  shipped.GetValueOrDefault((itemNo.ToUpperInvariant(), q.BaseDate)),
                    TodayUsed:     0,
                    PrevProduced:  Prod(itemNo, f.FgProcess, f.FgLine, prev),
                    PrevShipped:   shipped.GetValueOrDefault((itemNo.ToUpperInvariant(), prev)),
                    PrevUsed:      0);
                var opening = ActualsRules.Opening(snap);
                demand.Demand.TryGetValue(itemNo, out var dq);
                if ((dq is null || dq.All(v => v == 0)) && opening == 0 && !HasRegisteredPlan(itemNo, f)) continue;   // 수요·재고·등록계획 전부 0

                var mySlots = slots.Where(s => Eq(s.LineId, f.FgLine) && Eq(s.ItemNo, itemNo)).ToList();
                regWos.AddRange(mySlots.Select(s => new ApsRegisteredWo(KindAsm, itemNo, f.FgLine, Iso(s.Date), s.WoId, s.WoNumber, (double)s.Qty, s.ItemNo)));
                asmRows.Add(new AssemblyRow
                {
                    PartNo = itemNo, PartName = f.Item.ItemName, Alc = f.Item.Alc, Pgn = f.Item.Pgn, Model = f.Item.CarType,
                    LineCd = f.FgLine, Uph = 0, OpeningStock = opening, StockCounted = true,
                    Defect = 0, SisProduced = 0, Shipped = 0, SafetyStock = (double)f.Item.SafetyStock,
                    Days = dates.Select((d, i) =>
                    {
                        var dd = ApsCalendar.Parse(d);
                        var locked = mySlots.Where(s => s.Date == dd).Sum(s => s.Qty);
                        return new AssemblyDay { Date = d, T = 0, Demand = dq?[i] ?? 0, Supply = (double)locked, Locked = locked > 0 };
                    }).ToList(),
                });
                actuals.Add(ActualsRules.Display(snap));
            }
            var keptFg = new HashSet<string>(asmRows.Select(r => r.PartNo), StringComparer.OrdinalIgnoreCase);
            edges = edges.Where(e => keptFg.Contains(e.ParentPartNo)).ToList();
            var keptInj = new HashSet<string>(edges.Select(e => e.ChildPartNo), StringComparer.OrdinalIgnoreCase);

            // 사출품 현재 재고: 같은 품번(자기 간선) = WO 단계 WIP, BOM 자식 = 창고 재고(스펙 §4.4).
            // INJ 뒤에 라인 단계가 없는 라우팅(C)은 사출품이 곧 완제품(FG_Inventory 에 이미 있다)이라 WIP 0 — 단계 차가 누적 사출량 전체가 되는 유령 재고를 막는다
            bool SelfEdge(string child) => edges.Any(e => Eq(e.ParentPartNo, child) && Eq(e.ChildPartNo, child));
            var wip = ReadWipStock(conn, tx, keptInj.Where(c => SelfEdge(c) && fg[c].NextProcess is not null).ToList());
            var wh  = ReadWhStock(conn, tx, keptInj.Where(c => !SelfEdge(c)).ToList());

            // ── 사출 라인 × 날짜 능력(주간/야간) — 패턴은 APS 설정(라인 지정 → DEFAULT_PATTERN)이 정하고, 없으면 조회 차단(2026-10-06) ──
            var patterns  = new ApsPatternResolver(parsed, ReadPatternStatus(conn, tx, parsed));
            var lineDays  = new Dictionary<(string Line, string Date), LineDay>();
            var capWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LineDay Cap(string line, string date)
            {
                var key = (line.ToUpperInvariant(), date);
                if (lineDays.TryGetValue(key, out var ld)) return ld;
                if (patterns.Resolve(line) is not { } pat)
                {
                    // 미설정 — 행 계산은 빈 능력으로 이어가고 Cap 호출이 끝난 뒤 한 번에 ApsConfigurationException(라인 목록)으로 막는다
                    ld = new LineDay(new ShiftBands.DayNightBands(Array.Empty<SlotPacker.Interval>(), Array.Empty<SlotPacker.Interval>(), 6 * 60), 0, 0);
                    return lineDays[key] = ld;
                }
                var cap = LineScheduleRepository.ReadDayCapacity(conn, tx, line, Dt(ApsCalendar.Parse(date)), pat);
                var full = ShiftBands.Split(cap.ShiftBands ?? Array.Empty<(SlotPacker.Interval, int)>(), Array.Empty<SlotPacker.Interval>(), cap.DayStart);
                if (full.Day.Count + full.Night.Count == 0 && capWarned.Add(line))
                    warnings.Add($"라인 {line}: 패턴 {pat} 에 가동 구간이 없어 {date} 가동 시간을 0h 로 봅니다.");
                ld = new LineDay(full, full.DayHours, full.NightHours);
                return lineDays[key] = ld;
            }

            // ── 사출 행 ──
            var injRows = new List<InjectionRow>();
            var parentPlaced = new HashSet<(string, string)>();   // 자식 등록 계획으로 읽은 (부모 품번, 사출 라인) — 아래 미반영 경고에서 제외
            foreach (var child in keptInj.OrderBy(k => k, StringComparer.Ordinal))
            {
                var info = inj[child];
                items.TryGetValue(child, out var it);
                // 패밀리 = 같은 금형 · 같은 색상의 활성 품번(MD_MoldItem.Color) — 다른 색상은 같은 금형으로 따로 찍으므로 형제도 아니고 캐비티도 나누지 않는다
                var family      = moldsById[info.MoldId].Where(m => Eq(m.Color, info.Mold.Color)).ToList();
                var siblings    = family.Select(m => m.ItemNo).Where(x => !Eq(x, child)).OrderBy(x => x, StringComparer.Ordinal).ToList();
                int activeCount = Math.Max(1, family.Count);

                // 품번별 캐비티 = 금형 캐비티 ÷ 패밀리 품번 수(내림, 최소 1). 품번마다 다른 캐비티 수(비대칭 패밀리 금형)는 마스터에 없다 — PartCavityCount 는 09-30 폐지.
                // 나누어떨어지면(LH/RH 2캐비티 등) 정상이라 경고하지 않고, 나머지가 생길 때만(캐비티 3 에 품번 2) 내림값을 쓴다고 알린다
                int partCav = Math.Max(1, (info.Mold.MoldCavity ?? activeCount) / activeCount);
                if (activeCount > 1 && info.Mold.MoldCavity is int mcv && mcv % activeCount != 0)
                    warnings.Add($"사출품 {child}: 금형 {info.MoldId}{(info.Mold.Color is null ? "" : $"({info.Mold.Color})")} 은 품번 {activeCount}개를 같이 찍는데 캐비티 {mcv} 가 나누어떨어지지 않아 균등 분할({partCav})로 봅니다.");
                double uph = 0;
                if (info.MoldLine?.Uph is decimal mu && mu > 0)
                    uph = info.Mold.MoldCavity is int mc && mc > 0 ? (double)mu * partCav / mc : (double)mu;
                if (uph <= 0)
                    warnings.Add($"사출품 {child}: 금형 {info.MoldId} 의 {info.LineId} UPH 가 없어 가동시간을 계산할 수 없습니다.");
                int pack = it?.BoxQty is int bq && bq > 0 ? bq : 1;
                if (it?.BoxQty is not > 0)
                    warnings.Add($"사출품 {child}: BoxQty 가 없어(또는 0 이하) 포장 단위 1 로 봅니다.");

                var parents  = edges.Where(e => Eq(e.ChildPartNo, child)).ToList();
                decimal current = SelfEdge(child) ? wip.GetValueOrDefault(child) : wh.GetValueOrDefault(child);
                // 다음 단계 실적: 같은 품번은 라우팅의 INJ 다음 라인 단계 실적(간선 QtyPer 1) — 다음 단계가 없으면(라우팅 C) 완제품 라인의 INJ 실적 자체,
                // BOM 자식은 부모 완제품 공정·라인 실적
                decimal UsedOn(DateOnly d) => UsedQty(parents.Select(e =>
                {
                    var pf = fg[e.ParentPartNo];
                    var next = !Eq(e.ParentPartNo, child) ? Prod(e.ParentPartNo, pf.FgProcess, pf.FgLine, d)
                             : pf.NextProcess is null   ? Prod(child, pf.FgProcess, pf.FgLine, d)
                             :                            Prod(child, pf.NextProcess, pf.NextLine, d);
                    return (next, e.QtyPer);
                }));
                // 사출 실적은 라인을 가리지 않는다(같은 품번이 다른 사출기에서 찍혀도 재고다)
                var snap = new ActualsRules.Snapshot(child, current,
                    TodayProduced: Prod(child, MoldResolver.MoldProcessCode, null, q.BaseDate),
                    TodayShipped:  0,
                    TodayUsed:     UsedOn(q.BaseDate),
                    PrevProduced:  Prod(child, MoldResolver.MoldProcessCode, null, prev),
                    PrevShipped:   0,
                    PrevUsed:      UsedOn(prev));

                var mySlots = slots.Where(s => Eq(s.LineId, info.LineId) && Eq(s.ItemNo, child)).Select(s => (Slot: s, QtyPer: 1d)).ToList();
                foreach (var ps in ParentSlotsFor(child, info.LineId))
                {
                    mySlots.Add(ps);
                    parentPlaced.Add((ps.Slot.ItemNo.ToUpperInvariant(), info.LineId.ToUpperInvariant()));
                }
                regWos.AddRange(mySlots.Select(x => new ApsRegisteredWo(KindInj, child, info.LineId, Iso(x.Slot.Date), x.Slot.WoId, x.Slot.WoNumber, (double)x.Slot.Qty * x.QtyPer, x.Slot.ItemNo)));
                injRows.Add(new InjectionRow
                {
                    Group = info.LineId, PartNo = child, PartName = it?.ItemName, LineCd = info.LineId, Uph = uph,
                    OpeningStock = ActualsRules.Opening(snap), StockCounted = true, Defect = 0, SisProduced = 0, Used = 0,
                    SafetyStock = (double)(it?.SafetyStock ?? 0), MoldCode = info.MoldId, MoldColor = info.Mold.Color, Cavity = partCav, PackSize = pack,
                    SiblingPartNos = siblings,
                    Days = dates.Select(d =>
                    {
                        var dd = ApsCalendar.Parse(d);
                        double day = 0, night = 0;
                        foreach (var (s, qtyPer) in mySlots.Where(x => x.Slot.Date == dd))
                            if (ShiftBands.IsDay(Cap(info.LineId, d).Full, s.StartMin)) day += (double)s.Qty * qtyPer; else night += (double)s.Qty * qtyPer;
                        return new InjectionDay { Date = d, Requirement = 0, PlanDay = day, PlanNight = night, Locked = day + night > 0 };
                    }).ToList(),
                });
                actuals.Add(ActualsRules.Display(snap));
            }

            // 슬롯 없는 APS WO 는 셀의 Work Order 줄에 "미배치" 로만 보인다(10-06 사용자 결정 (b)) — 계획·부하에는 안 들어간다
            regWos.AddRange(ReadUnplacedApsWos(conn, tx, q.BaseDate, lastDate));

            // ── 반영하지 않은 등록 계획(§4.5·§9) — 부하로 넣지는 않되 조용히 버리지 않는다 ──
            // 행이 있는 품번의 다른 라인 슬롯·휴무일 슬롯, 비활성 품번 슬롯. 행이 없는 품번(다른 라인의 계획)의 슬롯은 대상이 아니다
            var placed   = new HashSet<(string, string)>(asmRows.Select(r => (r.PartNo.ToUpperInvariant(), r.LineCd!.ToUpperInvariant()))
                                                         .Concat(injRows.Select(r => (r.PartNo.ToUpperInvariant(), r.LineCd!.ToUpperInvariant())))
                                                         .Concat(parentPlaced));
            var rowItems = new HashSet<string>(asmRows.Select(r => r.PartNo).Concat(injRows.Select(r => r.PartNo)), StringComparer.OrdinalIgnoreCase);
            var dateSet  = new HashSet<string>(dates, StringComparer.Ordinal);
            foreach (var s in slots.OrderBy(s => s.Date).ThenBy(s => s.LineId, StringComparer.Ordinal).ThenBy(s => s.ItemNo, StringComparer.Ordinal))
            {
                var what = $"등록 계획 {s.ItemNo} {s.LineId} {Iso(s.Date)} {N(s.Qty)}개";
                if (!items.ContainsKey(s.ItemNo))
                    warnings.Add($"{what}: 활성 품번(MD_Item)이 아니라 계획에 반영하지 않았습니다.");
                else if (!rowItems.Contains(s.ItemNo))
                    continue;
                else if (!placed.Contains((s.ItemNo.ToUpperInvariant(), s.LineId.ToUpperInvariant())))
                    warnings.Add($"{what}: 이 품번의 계획 라인이 아니라 반영하지 않았습니다(그 라인 부하로도 넣지 않음).");
                else if (!dateSet.Contains(Iso(s.Date)))
                    warnings.Add($"{what}: 휴무일 슬롯이라 반영하지 않았습니다.");
            }

            // ── Settings: 라인별 교대 시간(최빈값 + 예외) · 완제품 DailyCap · 포장 규칙 ──
            var injLines = injRows.Select(r => r.LineCd!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (injectionMode && !injLines.Contains(q.LineId, StringComparer.OrdinalIgnoreCase)) injLines.Add(q.LineId);
            foreach (var line in injLines.OrderBy(l => l, StringComparer.Ordinal))
            {
                var perDate = dates.Select(d => (Date: d, Cap: Cap(line, d))).ToList();
                var mode = perDate.GroupBy(x => (x.Cap.DayH, x.Cap.NightH))
                                  .OrderByDescending(g => g.Count()).ThenBy(g => g.First().Date, StringComparer.Ordinal)
                                  .First().Key;
                settings.LineShifts.Add(new LineShift { LineCd = line, Day = mode.DayH, Night = mode.NightH, Stations = 1 });
                foreach (var x in perDate.Where(x => (x.Cap.DayH, x.Cap.NightH) != mode))
                    settings.ShiftExceptions.Add(new ShiftException { Date = x.Date, LineCd = line, Day = x.Cap.DayH, Night = x.Cap.NightH, Note = "라인 시간 패턴" });
            }
            // Cap 호출은 여기서 끝난다 — 패턴이 없는 사출 라인이 하나라도 있으면 결과를 내지 않는다(조용히 0h 로 계산하지 않는다)
            if (patterns.Problems.Count > 0) throw new ApsConfigurationException(patterns.Problems);
            var asmLines = asmRows.Select(r => r.LineCd!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!injectionMode && !allLines && !asmLines.Contains(q.LineId, StringComparer.OrdinalIgnoreCase)) asmLines.Add(q.LineId);
            foreach (var line in asmLines.OrderBy(l => l, StringComparer.Ordinal))
            {
                if (settings.LineShifts.Any(ls => Eq(ls.LineCd, line))) continue;   // INJ 전용 라우팅(C)의 완제품 행 — 사출 LineShift 가 이미 있다
                // NULL·0 이하는 "제한 없음"(AsmCapFor → null) — 0 을 그대로 넘기면 능력 0 으로 읽힌다
                int? cap = dailyCap.GetValueOrDefault(line) is int dc && dc > 0 ? dc : null;
                if (cap is null)
                    warnings.Add($"라인 {line}: MD_Line.DailyCap 이 없거나 0 이하({(dailyCap.GetValueOrDefault(line) is int raw ? raw.ToString(CultureInfo.InvariantCulture) : "NULL")})라 완제품 능력을 제한하지 않습니다.");
                settings.LineShifts.Add(new LineShift { LineCd = line, Day = 0, Night = 0, DailyCap = cap });
            }
            // 포장 규칙(§4.2 "품번마다"): 사출 행은 PackSize(BoxQty ?? 1), 사출 행이 아닌 완제품 행은 BoxQty ?? RoundTo.
            // BoxQty 없는 완제품 행에도 RoundTo 규칙을 두는 이유: PackFor 는 가장 긴 앞자리 규칙을 고르므로 규칙이 없으면 앞자리가 같은 다른 품번(85311-PI000 ↔ 85311-PI000MMN)의 포장 단위를 물려받는다
            settings.PackRules = injRows.Select(r => new PackRule(r.PartNo, r.PackSize)).ToList();
            foreach (var r in asmRows.Where(r => !injRows.Any(i => Eq(i.PartNo, r.PartNo))))
                settings.PackRules.Add(new PackRule(r.PartNo, items[r.PartNo].BoxQty is int bq && bq > 0 ? bq : settings.RoundTo));
            settings.UphRules = new();

            bundle.Assembly  = asmRows;
            bundle.Injection = injRows;
            bundle.Bom       = edges;
            tx.Commit();
            return Finish(bundle, settings, parsed.Options, actuals, warnings, cal, lines, demand.PlanDemand, regWos);
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>BOM 규칙 간선 QtyPer(스펙 §4.2) = QtyPer × (1 + ScrapPct/100).</summary>
    internal static double EdgeQtyPer(decimal qtyPer, decimal scrapPct) => (double)(qtyPer * (1 + scrapPct / 100m));
    /// <summary>BOM 규칙이 금형 품번을 찾아 내려가는 최대 깊이(완제품 바로 아래 = 0). 마스터 리스트는 2단계(서브조립 → 코어·레일)면 닿는다.</summary>
    internal const int BomWalkMaxDepth = 5;

    /// <summary>사출품 사용량(스펙 §4.4) = Σ 부모 간선마다 다음 단계 실적 × 간선 QtyPer(스크랩 포함). ActualsRules 는 이 곱을 받기만 한다 — 정본 테스트 ApsUsedQtyTests.</summary>
    internal static decimal UsedQty(IEnumerable<(decimal NextStepProduced, double QtyPer)> parents)
        => parents.Sum(p => p.NextStepProduced * (decimal)p.QtyPer);

    static ApsBuild Finish(PlanBundle bundle, Settings settings, ApsOptions options, List<ApsActualInfo> actuals,
                           List<string> warnings, IApsCalendar cal, Dictionary<string, LineInfo> lines,
                           IReadOnlyDictionary<string, double[]>? planDemand = null, IReadOnlyList<ApsRegisteredWo>? regWos = null)
        => new(bundle, settings, options, actuals, warnings, cal, new ShiftRules(settings, lines), new StageRules(settings, lines, foldPreHorizon: true), planDemand, regWos);

    /// <summary>
    /// 사출 라인·금형(스펙 §4.2): BOP INJ 스테이션 라인이 있으면 그 라인 — 배정 금형 중 교체 최소(동률 MoldID 순), 배정이 없으면 MoldID 순 첫 금형(UPH 없음은 호출자가 경고).
    /// 없으면 MD_MoldLine 중 COALESCE(PrepTime, MoldChangeMin, 0) 최소(동률 LineCode Ordinal → MoldID). 그것도 없으면 null.
    /// </summary>
    static InjInfo? ResolveInjection(string itemNo, List<MoldItemRow> molds,
        Dictionary<(string Line, string Mold), MoldLineRow> moldLineByKey,
        Dictionary<string, List<MoldLineRow>> moldLinesByMold, Dictionary<string, string> bopInj)
    {
        static decimal Change(MoldLineRow? ml, MoldItemRow m) => ml?.PrepTime ?? (decimal?)m.MoldChangeMin ?? 0m;

        if (bopInj.TryGetValue(itemNo, out var bopLine))
        {
            var pick = molds.Select(m => (M: m, Ml: moldLineByKey.GetValueOrDefault((bopLine.ToUpperInvariant(), m.MoldId.ToUpperInvariant()))))
                            .Where(x => x.Ml is not null)
                            .OrderBy(x => Change(x.Ml, x.M)).ThenBy(x => x.M.MoldId, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
            var m0 = pick.M ?? molds.OrderBy(m => m.MoldId, StringComparer.OrdinalIgnoreCase).First();
            return new InjInfo(itemNo, bopLine, m0.MoldId, m0, pick.Ml);
        }
        var best = molds.SelectMany(m => moldLinesByMold.GetValueOrDefault(m.MoldId, new()).Select(ml => (M: m, Ml: ml)))
                        .OrderBy(x => Change(x.Ml, x.M))
                        .ThenBy(x => x.Ml.LineCode, StringComparer.Ordinal)
                        .ThenBy(x => x.M.MoldId, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
        return best.Ml is null ? null : new InjInfo(itemNo, best.Ml.LineCode, best.M.MoldId, best.M, best.Ml);
    }

    // ── 설정 (공통코드 + MD_ApsLineStage → ApsSettingsLoader.Parse) ─────
    /// <summary>Aps/ 아래에 SqlClient 를 두지 않기 위해 여기서 읽는다. 파싱·기본값·경고는 ApsSettingsLoader.Parse.</summary>
    internal static ApsSettingsLoader.Parsed ReadSettings(SqlConnection conn, SqlTransaction? tx)
    {
        using var cmd = new SqlCommand("""
            SELECT c.GroupCode, c.CodeValue, c.Attribute1, c.SortOrder, ISNULL(c.UseFlag, 1) AS UseFlag
            FROM   dbo.MD_CodeItem c
            WHERE  c.GroupCode IN (@G1, @G2);

            SELECT s.LineID, s.OffsetDays, s.UseStock, s.Note, s.PatternID
            FROM   dbo.MD_ApsLineStage s
            ORDER  BY s.LineID;
            """, conn, tx);
        cmd.Parameters.Add("@G1", SqlDbType.VarChar, 20).Value = ApsSettingsLoader.GroupSetting;
        cmd.Parameters.Add("@G2", SqlDbType.VarChar, 20).Value = ApsSettingsLoader.GroupCoverTier;
        using var rdr = cmd.ExecuteReader();
        var codes = new List<ApsSettingsLoader.CodeRow>();
        while (rdr.Read())
            codes.Add(new((string)rdr["GroupCode"], (string)rdr["CodeValue"], rdr["Attribute1"] as string,
                          rdr["SortOrder"] as int?, (bool)rdr["UseFlag"]));
        var stages = new List<ApsSettingsLoader.LineStageRow>();
        if (rdr.NextResult())
            while (rdr.Read())
                stages.Add(new((string)rdr["LineID"], (int)rdr["OffsetDays"], (bool)rdr["UseStock"], rdr["Note"] as string, rdr["PatternID"] as string));
        return ApsSettingsLoader.Parse(codes, stages);
    }

    /// <summary>설정이 가리키는 패턴(기본 + 라인 지정)의 존재·상태 — ApsPatternResolver 에 넘긴다. 없는 ID 는 사전에 없다.</summary>
    internal static Dictionary<string, string?> ReadPatternStatus(SqlConnection conn, SqlTransaction? tx, ApsSettingsLoader.Parsed parsed)
    {
        var ids = parsed.LinePatterns.Values.Append(parsed.DefaultPatternId)
                        .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return map;
        using var cmd = new SqlCommand("", conn, tx);
        var names = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            names.Add($"@P{i}");
            cmd.Parameters.Add($"@P{i}", SqlDbType.VarChar, 20).Value = ids[i];
        }
        cmd.CommandText = $"SELECT PatternID, Status FROM dbo.MD_LineTimePattern WHERE PatternID IN ({string.Join(", ", names)});";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) map[(string)rdr["PatternID"]] = rdr["Status"] as string;
        return map;
    }

    // ── 읽기 헬퍼 (트랜잭션 안, 파라미터 형 명시) ──────────────────────
    static List<LineRow> ReadLines(SqlConnection conn, SqlTransaction? tx)
    {
        using var cmd = new SqlCommand("""
            SELECT l.LineID, l.LineName, wc.ProcessCode
            FROM   dbo.MD_Line l
            JOIN   dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
            WHERE  ISNULL(l.Status,'ACTIVE') <> 'INACTIVE'
              AND  wc.ProcessCode IN ('INJ','IMG','PNT');
            """, conn, tx);
        using var rdr = cmd.ExecuteReader();
        var list = new List<LineRow>();
        while (rdr.Read())
            list.Add(new((string)rdr["LineID"], rdr["LineName"] as string, (string)rdr["ProcessCode"]));
        // 콜레이션 정렬은 '-' 를 건너뛰므로 순서 계약(LineID Ordinal)은 여기서 맞춘다
        return list.OrderBy(l => l.LineId, StringComparer.Ordinal).ToList();
    }

    static Dictionary<string, ItemRow> ReadItems(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            SELECT i.ItemNo, i.ItemName, i.ItemType, ISNULL(i.InjFlag, 0) AS InjFlag, i.RoutingType, ISNULL(i.SafetyStock, 0) AS SafetyStock, i.PGN, i.ALC, i.CarType, i.BoxQty
            FROM   dbo.MD_Item i
            WHERE  ISNULL(i.ActiveFlag, 1) = 1;
            """, conn, tx);
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, ItemRow>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read())
            map[(string)rdr["ItemNo"]] = new((string)rdr["ItemNo"], rdr["ItemName"] as string, rdr["ItemType"] as string, Convert.ToBoolean(rdr["InjFlag"]), rdr["RoutingType"] as string,
                                             Convert.ToDecimal(rdr["SafetyStock"]), rdr["PGN"] as string, rdr["ALC"] as string,
                                             rdr["CarType"] as string, rdr["BoxQty"] as int?);
        return map;
    }

    /// <summary>수주 행(상태·고객 필터는 DemandRules 가 한다 — 규칙이 한 곳). 납기 상한만 자른다(하한 없음 = 지연분 포함, NULL 납기는 경고용으로 읽는다).</summary>
    static List<DemandRules.OrderRow> ReadOrders(SqlConnection conn, SqlTransaction tx, DateOnly dueTo)
    {
        using var cmd = new SqlCommand("""
            SELECT s.SoID, s.SoNumber, s.SoLineNo, s.CustomerID, s.ItemNo, s.RequestedDeliveryDate,
                   ISNULL(s.OrderQty, 0) AS OrderQty, ISNULL(s.ShippedQty, 0) AS ShippedQty, s.Status
            FROM   dbo.PP_CustomerOrder s
            WHERE  s.Status IN (@Confirmed, @Open) AND s.ItemNo IS NOT NULL
              AND  (s.RequestedDeliveryDate IS NULL OR s.RequestedDeliveryDate <= @To);
            """, conn, tx);
        cmd.Parameters.Add("@Confirmed", SqlDbType.VarChar, 20).Value = DemandRules.StatusConfirmed;
        cmd.Parameters.Add("@Open",      SqlDbType.VarChar, 20).Value = DemandRules.StatusOpen;
        cmd.Parameters.Add("@To",        SqlDbType.Date).Value        = Dt(dueTo);
        using var rdr = cmd.ExecuteReader();
        var list = new List<DemandRules.OrderRow>();
        while (rdr.Read())
            list.Add(new((int)rdr["SoID"], rdr["SoNumber"] as string, rdr["SoLineNo"] as int?, rdr["CustomerID"] as string, (string)rdr["ItemNo"],
                         rdr["RequestedDeliveryDate"] is DateTime due ? DateOnly.FromDateTime(due) : null,
                         Convert.ToDecimal(rdr["OrderQty"]), Convert.ToDecimal(rdr["ShippedQty"]), rdr["Status"] as string));
        return list;
    }

    /// <summary>일별 구매계획(스펙 2026-09-29 §7): 기준일~마지막 날, 0 이하는 저장되지 않지만 방어적으로 다시 걸러낸다.</summary>
    static List<DemandRules.PlanRow> ReadDemandPlan(SqlConnection conn, SqlTransaction tx, DateOnly from, DateOnly to)
    {
        using var cmd = new SqlCommand("""
            SELECT CustomerID, ItemNo, PlanDate, ScheduledQty
            FROM   dbo.PP_DemandPlan
            WHERE  PlanDate BETWEEN @From AND @To AND ScheduledQty > 0;
            """, conn, tx);
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = Dt(from);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = Dt(to);
        using var rdr = cmd.ExecuteReader();
        var list = new List<DemandRules.PlanRow>();
        while (rdr.Read())
            list.Add(new(rdr["CustomerID"] as string, (string)rdr["ItemNo"], DateOnly.FromDateTime((DateTime)rdr["PlanDate"]), Convert.ToDecimal(rdr["ScheduledQty"])));
        return list;
    }

    /// <summary>등록 계획(스펙 §4.5): 기준일~마지막 날의 WO 슬롯(취소 WO 제외, 수량 &gt; 0). 0분 형제 슬롯도 수량은 센다.</summary>
    static List<SlotRow> ReadRegisteredSlots(SqlConnection conn, SqlTransaction tx, DateOnly from, DateOnly to)
    {
        using var cmd = new SqlCommand("""
            SELECT s.LineID, s.ScheduleDate, w.ItemNo, ISNULL(s.StartMin, 0) AS StartMin, ISNULL(s.PlannedQty, 0) AS PlannedQty, w.WoID, w.WoNumber
            FROM   dbo.PP_LineSchedule s
            JOIN   dbo.PP_WorkOrder w ON w.WoID = s.WoID
            WHERE  s.EntryType = 'WO' AND ISNULL(w.Status, '') <> 'Cancelled'
              AND  s.ScheduleDate BETWEEN @From AND @To
              AND  s.LineID IS NOT NULL AND w.ItemNo IS NOT NULL
              AND  ISNULL(s.PlannedQty, 0) > 0;
            """, conn, tx);
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = Dt(from);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = Dt(to);
        using var rdr = cmd.ExecuteReader();
        var list = new List<SlotRow>();
        while (rdr.Read())
            list.Add(new((string)rdr["LineID"], DateOnly.FromDateTime((DateTime)rdr["ScheduleDate"]), (string)rdr["ItemNo"],
                         Convert.ToInt32(rdr["StartMin"]), Convert.ToDecimal(rdr["PlannedQty"]), Convert.ToInt32(rdr["WoID"]), (string)rdr["WoNumber"]));
        return list;
    }

    /// <summary>
    /// 슬롯을 못 받은 APS WO(취소·완료 제외, PP_LineSchedule 행 없음) — 사출 행 항목은 PP_ApsRunWo 가 가리키는 계획 행(자식 품번·사출일·라인, Qty = 자식 단위),
    /// 완제품 행 항목은 WO 마다 1건(품번 = WO 품번, 날짜 = ProdDeadline = 사출일 + 선행일 = 공급일, Qty = OrderQty). 기간 밖 날짜는 뺀다.
    /// </summary>
    static List<ApsRegisteredWo> ReadUnplacedApsWos(SqlConnection conn, SqlTransaction tx, DateOnly from, DateOnly to)
    {
        using var cmd = new SqlCommand("""
            SELECT x.WoID, w.WoNumber, w.ItemNo AS WoItem, ISNULL(w.OrderQty, 0) AS OrderQty, w.ProdDeadline,
                   l.Kind, l.ItemNo AS LineItem, l.LineID, l.PlanDate, ISNULL(x.Qty, 0) AS Qty
            FROM   dbo.PP_ApsRunWo x
            JOIN   dbo.PP_WorkOrder   w ON w.WoID = x.WoID
            JOIN   dbo.PP_ApsPlanLine l ON l.PlanLineID = x.PlanLineID
            WHERE  ISNULL(w.Status, '') NOT IN ('Cancelled', 'Completed', 'Closed', 'Stocked')
              AND  NOT EXISTS (SELECT 1 FROM dbo.PP_LineSchedule s WHERE s.WoID = w.WoID)
              AND  (l.PlanDate BETWEEN @From AND @To OR CAST(w.ProdDeadline AS date) BETWEEN @From AND @To)
            ORDER  BY w.WoID, l.PlanLineID;
            """, conn, tx);
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = Dt(from);
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = Dt(to);
        using var rdr = cmd.ExecuteReader();
        var list = new List<ApsRegisteredWo>();
        var asmDone = new HashSet<int>();
        while (rdr.Read())
        {
            int woId = (int)rdr["WoID"]; var woNo = (string)rdr["WoNumber"]; var woItem = (string)rdr["WoItem"];
            var planDate = DateOnly.FromDateTime((DateTime)rdr["PlanDate"]);
            if (planDate >= from && planDate <= to && Eq(rdr["Kind"] as string, KindInj))
                list.Add(new ApsRegisteredWo(KindInj, (string)rdr["LineItem"], (string)rdr["LineID"], Iso(planDate), woId, woNo, Convert.ToDouble(rdr["Qty"]), woItem, Unplaced: true));
            if (rdr["ProdDeadline"] is DateTime dl && asmDone.Add(woId))
            {
                var d = DateOnly.FromDateTime(dl);
                if (d >= from && d <= to)
                    list.Add(new ApsRegisteredWo(KindAsm, woItem, "", Iso(d), woId, woNo, Convert.ToDouble(rdr["OrderQty"]), woItem, Unplaced: true));
            }
        }
        return list;
    }

    /// <summary>완제품 현재 재고(품번별) = 통합재고 WH_Inventory 의 PartNo 별 SUM(Qty), Qty > 0 — 위치(LocationNo)는 보지 않는다(사용자 결정 09-30). FG_Inventory 는 통합재고로 흡수돼 없다.</summary>
    static Dictionary<string, decimal> ReadFgStock(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            SELECT f.PartNo AS ItemNo, SUM(f.Qty) AS Qty
            FROM   dbo.WH_Inventory f
            WHERE  f.PartNo IS NOT NULL AND f.Qty > 0
            GROUP  BY f.PartNo;
            """, conn, tx);
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read()) map[(string)rdr["ItemNo"]] = Convert.ToDecimal(rdr["Qty"]);
        return map;
    }

    static List<MoldItemRow> ReadMoldItems(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            SELECT mi.MoldID, mi.ItemNo, mi.Color, m.CavityCount AS MoldCavityCount, m.MoldChangeMin
            FROM   dbo.MD_MoldItem mi
            JOIN   dbo.MD_Mold m ON m.MoldID = mi.MoldID
            WHERE  ISNULL(mi.ActiveFlag, 1) = 1
            ORDER  BY mi.MoldID, mi.ItemNo;
            """, conn, tx);
        using var rdr = cmd.ExecuteReader();
        var list = new List<MoldItemRow>();
        while (rdr.Read())
            list.Add(new((string)rdr["MoldID"], (string)rdr["ItemNo"], rdr["Color"] as string, rdr["MoldCavityCount"] as int?, rdr["MoldChangeMin"] as int?));
        return list;
    }

    /// <summary>활성 INJ 라인의 금형 배정만.</summary>
    static List<MoldLineRow> ReadMoldLines(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            SELECT ml.LineCode, ml.MoldID, ml.UPH, ml.PrepTime
            FROM   dbo.MD_MoldLine ml
            JOIN   dbo.MD_Line       l  ON l.LineID = ml.LineCode
            JOIN   dbo.MD_WorkCenter wc ON wc.WCID  = l.WCID
            WHERE  wc.ProcessCode = @Inj AND ISNULL(l.Status, 'ACTIVE') <> 'INACTIVE'
            ORDER  BY ml.MoldID, ml.LineCode;
            """, conn, tx);
        cmd.Parameters.Add("@Inj", SqlDbType.VarChar, 10).Value = MoldResolver.MoldProcessCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<MoldLineRow>();
        while (rdr.Read())
            list.Add(new((string)rdr["LineCode"], (string)rdr["MoldID"],
                         rdr["UPH"] is decimal u ? u : null, rdr["PrepTime"] is decimal p ? p : null));
        return list;
    }

    /// <summary>품번 → BOP INJ 스테이션의 라인(활성 라인, StepSeq 순 첫 행). ReadPreview 의 BopLineID 하위질의와 같은 조인.</summary>
    static Dictionary<string, string> ReadBopInjLines(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("""
            SELECT b.ItemNo, st.LineID, b.StepSeq
            FROM   dbo.MD_Bop b
            JOIN   dbo.MD_Station    st ON st.StationCode = b.StationCode
            JOIN   dbo.MD_Line       sl ON sl.LineID      = st.LineID
            JOIN   dbo.MD_WorkCenter sw ON sw.WCID        = sl.WCID
            WHERE  ISNULL(b.ActiveFlag, 1) = 1 AND sw.ProcessCode = @Inj
              AND  ISNULL(sl.Status, 'ACTIVE') <> 'INACTIVE'
            ORDER  BY b.ItemNo, b.StepSeq;
            """, conn, tx);
        cmd.Parameters.Add("@Inj", SqlDbType.VarChar, 10).Value = MoldResolver.MoldProcessCode;
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read())
        {
            var item = (string)rdr["ItemNo"];
            if (!map.ContainsKey(item)) map[item] = (string)rdr["LineID"];
        }
        return map;
    }

    static Dictionary<string, List<BomRow>> ReadBom(SqlConnection conn, SqlTransaction tx, DateOnly today)
    {
        using var cmd = new SqlCommand(PpRepository.EffectiveBomSql, conn, tx);
        cmd.Parameters.Add("@Today", SqlDbType.Date).Value = Dt(today);
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, List<BomRow>>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read())
        {
            var row = new BomRow((string)rdr["ParentItemNo"], (string)rdr["CompItemNo"],
                                 Convert.ToDecimal(rdr["QtyPer"]), Convert.ToDecimal(rdr["ScrapPct"]));
            if (!map.TryGetValue(row.Parent, out var list)) map[row.Parent] = list = new();
            list.Add(row);
        }
        return map;
    }

    /// <summary>품번 목록을 1,000개씩 잘라 명령을 만든다 — IN (@I0, @I1, …) 파라미터 목록(STRING_SPLIT 은 호환성 수준 130 이 필요해 쓰지 않는다; 파라미터 한도 2,100).</summary>
    static IEnumerable<SqlCommand> ChunkedIn(SqlConnection conn, SqlTransaction tx, IReadOnlyList<string> items,
                                             Func<string, string> sqlOf, Action<SqlCommand> addParams)
    {
        foreach (var chunk in items.Chunk(1000))
        {
            var cmd = new SqlCommand { Connection = conn, Transaction = tx };
            var names = new string[chunk.Length];
            for (int i = 0; i < chunk.Length; i++)
            {
                names[i] = "@I" + i.ToString(CultureInfo.InvariantCulture);
                cmd.Parameters.Add(names[i], SqlDbType.VarChar, 20).Value = chunk[i];
            }
            cmd.CommandText = sqlOf(string.Join(",", names));
            addParams(cmd);
            yield return cmd;
        }
    }

    /// <summary>(품번, 공정, 라인, 전기일) 별 SUM(GoodQty) — 역분개 −1 행 포함 순생산. 전기일 = 직전 근무일·기준일 두 날만.</summary>
    static Dictionary<(string Item, string Proc, string? Line, DateOnly Date), decimal> ReadProduced(
        SqlConnection conn, SqlTransaction tx, IReadOnlyList<string> items, DateOnly prev, DateOnly baseDate)
    {
        var map = new Dictionary<(string, string, string?, DateOnly), decimal>();
        foreach (var cmd in ChunkedIn(conn, tx, items, inList => $"""
            SELECT w.ItemNo, r.ProcessCode, r.LineID, r.ProdDate, ISNULL(SUM(ISNULL(r.GoodQty, 0)), 0) AS Qty
            FROM   dbo.PR_ProductionResult r
            JOIN   dbo.PP_WorkOrder w ON w.WoID = r.WoID
            WHERE  r.ProdDate IN (@D0, @D1) AND r.ProcessCode IS NOT NULL
              AND  w.ItemNo IN ({inList})
            GROUP  BY w.ItemNo, r.ProcessCode, r.LineID, r.ProdDate;
            """, c =>
            {
                c.Parameters.Add("@D0", SqlDbType.Date).Value = Dt(baseDate);
                c.Parameters.Add("@D1", SqlDbType.Date).Value = Dt(prev);
            }))
        {
            using (cmd)
            using (var rdr = cmd.ExecuteReader())
                while (rdr.Read())
                    map[((string)rdr["ItemNo"], (string)rdr["ProcessCode"], rdr["LineID"] as string, DateOnly.FromDateTime((DateTime)rdr["ProdDate"]))]
                        = Convert.ToDecimal(rdr["Qty"]);
        }
        return map;
    }

    /// <summary>(품번(대문자), 일자) 별 출고 수량 = WH_InventoryTransaction 의 OUT 거래(QtyChange &lt; 0) 합, TransactionTime 날짜 기준, 위치·사유 무관.
    /// 이 값은 "그날 재고에서 빠진 양을 아침 재고로 되돌리는" 용도라 실제로 재고를 줄인 거래만 센다(구 FG_LoadingConfirm·FG_PickingDetail 은 통합재고 이관으로 없다).</summary>
    static Dictionary<(string Item, DateOnly Date), decimal> ReadShipped(
        SqlConnection conn, SqlTransaction tx, IReadOnlyList<string> items, DateOnly from, DateOnly to)
    {
        var map = new Dictionary<(string, DateOnly), decimal>();
        foreach (var cmd in ChunkedIn(conn, tx, items, inList => $"""
            SELECT t.ItemNo, CAST(t.TransactionTime AS date) AS D, SUM(-t.QtyChange) AS Qty
            FROM   dbo.WH_InventoryTransaction t
            WHERE  t.TransactionType = 'OUT' AND t.QtyChange < 0
              AND  t.TransactionTime >= @From AND t.TransactionTime < DATEADD(day, 1, @To)
              AND  t.ItemNo IN ({inList})
            GROUP  BY t.ItemNo, CAST(t.TransactionTime AS date);
            """, c =>
            {
                c.Parameters.Add("@From", SqlDbType.Date).Value = Dt(from);
                c.Parameters.Add("@To",   SqlDbType.Date).Value = Dt(to);
            }))
        {
            using (cmd)
            using (var rdr = cmd.ExecuteReader())
                while (rdr.Read())
                    map[(((string)rdr["ItemNo"]).ToUpperInvariant(), DateOnly.FromDateTime((DateTime)rdr["D"]))] = Convert.ToDecimal(rdr["Qty"]);
        }
        return map;
    }

    /// <summary>같은 품번 사출품의 현재 재고 근사(스펙 §4.4): WO 마다 INJ 단계 CompletedQty − INJ 다음 라인 단계 CompletedQty(음수 0), 취소 제외. RAW LOT 미포함.</summary>
    static Dictionary<string, decimal> ReadWipStock(SqlConnection conn, SqlTransaction tx, IReadOnlyList<string> items)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in ChunkedIn(conn, tx, items, inList => $"""
            SELECT w.ItemNo, ISNULL(SUM(CASE WHEN x.Wip > 0 THEN x.Wip ELSE 0 END), 0) AS Qty
            FROM   dbo.PP_WorkOrder w
            OUTER APPLY (SELECT MIN(ri.StepSeq) AS InjSeq
                         FROM   dbo.PP_WorkOrderRouting ri
                         WHERE  ri.WoID = w.WoID AND ri.ProcessCode = @Inj) s
            OUTER APPLY (SELECT MIN(rn.StepSeq) AS NextSeq
                         FROM   dbo.PP_WorkOrderRouting rn
                         WHERE  rn.WoID = w.WoID AND rn.LineID IS NOT NULL AND rn.StepSeq > s.InjSeq) n
            CROSS APPLY (SELECT ISNULL((SELECT SUM(a.CompletedQty) FROM dbo.PP_WorkOrderRouting a WHERE a.WoID = w.WoID AND a.StepSeq = s.InjSeq), 0)
                              - ISNULL((SELECT SUM(b.CompletedQty) FROM dbo.PP_WorkOrderRouting b WHERE b.WoID = w.WoID AND b.StepSeq = n.NextSeq), 0) AS Wip) x
            WHERE  ISNULL(w.Status, '') <> 'Cancelled' AND s.InjSeq IS NOT NULL
              AND  w.ItemNo IN ({inList})
            GROUP  BY w.ItemNo;
            """, c => c.Parameters.Add("@Inj", SqlDbType.VarChar, 10).Value = MoldResolver.MoldProcessCode))
        {
            using (cmd)
            using (var rdr = cmd.ExecuteReader())
                while (rdr.Read()) map[(string)rdr["ItemNo"]] = Convert.ToDecimal(rdr["Qty"]);
        }
        return map;
    }

    /// <summary>BOM 자식 사출품의 현재 재고 = 통합재고 WH_Inventory 의 PartNo 별 SUM(Qty), Qty > 0, 위치 무관(완제품과 같은 규칙).</summary>
    static Dictionary<string, decimal> ReadWhStock(SqlConnection conn, SqlTransaction tx, IReadOnlyList<string> items)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in ChunkedIn(conn, tx, items, inList => $"""
            SELECT x.PartNo AS ItemNo, SUM(x.Qty) AS Qty
            FROM   dbo.WH_Inventory x
            WHERE  x.PartNo IN ({inList}) AND x.Qty > 0
            GROUP  BY x.PartNo;
            """, _ => { }))
        {
            using (cmd)
            using (var rdr = cmd.ExecuteReader())
                while (rdr.Read()) map[(string)rdr["ItemNo"]] = Convert.ToDecimal(rdr["Qty"]);
        }
        return map;
    }

    // ── 실행 저장·조회 (스펙 §3.4·§7) ─────────────────────────────────────
    public sealed record ApsRunRow(int RunId, string LineId, DateOnly BaseDate, int Days, string? CustomerId, bool IncludeOpen,
                                   string Status, int WarningCount, string CreatedBy, DateTime CreatedTs, string? ModifiedBy, DateTime? ModifiedTs,
                                   bool IncludeDailyPlan = false);
    public sealed record ApsRunDetail(ApsRunRow Row, string SettingsJson, string BundleJson, string ResultJson);
    public sealed record ApsPlanLineRow(int PlanLineId, int RunId, string Kind, string ItemNo, string LineId, DateOnly PlanDate,
                                        decimal Demand, decimal Supply, decimal Requirement, decimal PlanDay, decimal PlanNight,
                                        decimal Stock, bool Locked, string Status, int? WoId, bool SameItem = false);   // SameItem = 같은 품번 규칙 사출 행(자기 간선 BomEdge(X,X,1)) — 「WO 생성」 대상, ASM 행은 false
    public sealed record ApsRunSave(ApsQuery Query, string SettingsJson, string BundleJson, string ResultJson,
                                    IReadOnlyList<ApsPlanLineRow> Lines, int WarningCount);

    const string RunColumns = "RunID, LineID, BaseDate, Days, CustomerID, IncludeOpen, Status, WarningCount, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS, IncludeDailyPlan";
    const string PlanLineColumns = "PlanLineID, RunID, Kind, ItemNo, LineID, PlanDate, Demand, Supply, Requirement, PlanDay, PlanNight, Stock, Locked, Status, WoID, SameItem";

    /// <summary>
    /// 한 트랜잭션: PP_ApsRun(Status Saved, JSON 3개) INSERT → OUTPUT RunID → PP_ApsPlanLine 일괄 INSERT(명령 1개, 파라미터 교체). 반환 RunID.
    /// 같은 라인·기준일에 이미 실행이 있어도 새 행을 만든다(이력). 행위자는 varchar(20) — ActorCode.Of 결과만 넘긴다.
    /// </summary>
    public int SaveRun(ApsRunSave save, string actor)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            int runId;
            using (var ins = new SqlCommand("""
                INSERT INTO dbo.PP_ApsRun
                       (LineID, BaseDate, Days, CustomerID, IncludeOpen, Status, SettingsJson, BundleJson, ResultJson, WarningCount, CreatedBy, CreatedTS, IncludeDailyPlan)
                OUTPUT INSERTED.RunID
                VALUES (@Line, @Base, @Days, @Cust, @Open, @Status, @S, @B, @R, @Warn, @By, SYSDATETIME(), @Daily);
                """, conn, tx))
            {
                ins.Parameters.Add("@Line",   SqlDbType.VarChar, 20).Value  = save.Query.LineId;
                ins.Parameters.Add("@Base",   SqlDbType.Date).Value         = Dt(save.Query.BaseDate);
                ins.Parameters.Add("@Days",   SqlDbType.Int).Value          = save.Query.Days;
                ins.Parameters.Add("@Cust",   SqlDbType.VarChar, 20).Value  = (object?)save.Query.CustomerId ?? DBNull.Value;
                ins.Parameters.Add("@Open",   SqlDbType.Bit).Value          = save.Query.IncludeOpen;
                ins.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value  = StatusSaved;
                ins.Parameters.Add("@S",      SqlDbType.NVarChar, -1).Value = save.SettingsJson;
                ins.Parameters.Add("@B",      SqlDbType.NVarChar, -1).Value = save.BundleJson;
                ins.Parameters.Add("@R",      SqlDbType.NVarChar, -1).Value = save.ResultJson;
                ins.Parameters.Add("@Warn",   SqlDbType.Int).Value          = save.WarningCount;
                ins.Parameters.Add("@By",     SqlDbType.NVarChar, 20).Value = actor;
                ins.Parameters.Add("@Daily",  SqlDbType.Bit).Value          = save.Query.IncludeDailyPlan;
                runId = (int)ins.ExecuteScalar()!;
            }

            using (var line = new SqlCommand("""
                INSERT INTO dbo.PP_ApsPlanLine
                       (RunID, Kind, ItemNo, LineID, PlanDate, Demand, Supply, Requirement, PlanDay, PlanNight, Stock, Locked, Status, WoID, SameItem)
                VALUES (@Run, @Kind, @Item, @Line, @Date, @Dem, @Sup, @Req, @Day, @Night, @Stock, @Locked, @Status, NULL, @SameItem);
                """, conn, tx))
            {
                line.Parameters.Add("@Run",    SqlDbType.Int).Value = runId;
                line.Parameters.Add("@Kind",   SqlDbType.Char, 3);
                line.Parameters.Add("@Item",   SqlDbType.VarChar, 20);
                line.Parameters.Add("@Line",   SqlDbType.VarChar, 20);
                line.Parameters.Add("@Date",   SqlDbType.Date);
                foreach (var name in new[] { "@Dem", "@Sup", "@Req", "@Day", "@Night", "@Stock" })
                {
                    var p = line.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 14; p.Scale = 3;
                }
                line.Parameters.Add("@Locked", SqlDbType.Bit);
                line.Parameters.Add("@Status", SqlDbType.VarChar, 10);
                line.Parameters.Add("@SameItem", SqlDbType.Bit);
                foreach (var r in save.Lines)
                {
                    line.Parameters["@Kind"].Value   = r.Kind;
                    line.Parameters["@Item"].Value   = r.ItemNo;
                    line.Parameters["@Line"].Value   = r.LineId;
                    line.Parameters["@Date"].Value   = Dt(r.PlanDate);
                    line.Parameters["@Dem"].Value    = r.Demand;
                    line.Parameters["@Sup"].Value    = r.Supply;
                    line.Parameters["@Req"].Value    = r.Requirement;
                    line.Parameters["@Day"].Value    = r.PlanDay;
                    line.Parameters["@Night"].Value  = r.PlanNight;
                    line.Parameters["@Stock"].Value  = r.Stock;
                    line.Parameters["@Locked"].Value = r.Locked;
                    line.Parameters["@Status"].Value = r.Status;
                    line.Parameters["@SameItem"].Value = r.SameItem;
                    line.ExecuteNonQuery();
                }
            }
            tx.Commit();
            return runId;
        }
        catch { tx.Rollback(); throw; }
    }

    public ApsRunDetail? LoadRun(int runId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand($"SELECT {RunColumns}, SettingsJson, BundleJson, ResultJson FROM dbo.PP_ApsRun WHERE RunID = @Run;", conn);
        cmd.Parameters.Add("@Run", SqlDbType.Int).Value = runId;
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return new ApsRunDetail(MapRun(rdr), (string)rdr["SettingsJson"], (string)rdr["BundleJson"], (string)rdr["ResultJson"]);
    }

    /// <summary>최근 실행(RunID DESC). 실행 이력 다이얼로그가 50건을 본다.</summary>
    public List<ApsRunRow> ListRuns(int top = 50)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand($"SELECT TOP (@Top) {RunColumns} FROM dbo.PP_ApsRun ORDER BY RunID DESC;", conn);
        cmd.Parameters.Add("@Top", SqlDbType.Int).Value = Math.Max(1, top);
        using var rdr = cmd.ExecuteReader();
        var list = new List<ApsRunRow>();
        while (rdr.Read()) list.Add(MapRun(rdr));
        return list;
    }

    /// <summary>실행의 정규화 계획 행 — Kind(ASM→INJ), ItemNo, PlanDate 순.</summary>
    public List<ApsPlanLineRow> ListPlanLines(int runId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand($"SELECT {PlanLineColumns} FROM dbo.PP_ApsPlanLine WHERE RunID = @Run ORDER BY Kind, ItemNo, PlanDate;", conn);
        cmd.Parameters.Add("@Run", SqlDbType.Int).Value = runId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ApsPlanLineRow>();
        while (rdr.Read()) list.Add(MapPlanLine(rdr));
        return list;
    }

    static ApsRunRow MapRun(SqlDataReader r) => new(
        (int)r["RunID"], (string)r["LineID"], DateOnly.FromDateTime((DateTime)r["BaseDate"]), (int)r["Days"],
        r["CustomerID"] as string, (bool)r["IncludeOpen"], (string)r["Status"], (int)r["WarningCount"],
        (string)r["CreatedBy"], (DateTime)r["CreatedTS"], r["ModifiedBy"] as string, r["ModifiedTS"] as DateTime?,
        (bool)r["IncludeDailyPlan"]);

    static ApsPlanLineRow MapPlanLine(SqlDataReader r) => new(
        (int)r["PlanLineID"], (int)r["RunID"], ((string)r["Kind"]).Trim(), (string)r["ItemNo"], (string)r["LineID"],
        DateOnly.FromDateTime((DateTime)r["PlanDate"]),
        r.GetDecimal(r.GetOrdinal("Demand")), r.GetDecimal(r.GetOrdinal("Supply")), r.GetDecimal(r.GetOrdinal("Requirement")),
        r.GetDecimal(r.GetOrdinal("PlanDay")), r.GetDecimal(r.GetOrdinal("PlanNight")), r.GetDecimal(r.GetOrdinal("Stock")),
        (bool)r["Locked"], (string)r["Status"], r["WoID"] as int?, (bool)r["SameItem"]);

    // ── MD_ApsLineStage (스펙 §3.2, PP-APS 설정 다이얼로그) ────────────────
    /// <summary>PatternId = 이 라인의 APS 가동 시간 패턴(NULL = DEFAULT_PATTERN 을 따른다), PatternName = 표시용(패턴이 지워졌으면 NULL).</summary>
    public sealed record ApsLineStageRow(string LineId, string? LineName, string LineType, int OffsetDays, bool UseStock, string? Note,
                                         string CreatedBy, DateTime CreatedTs, string? ModifiedBy, DateTime? ModifiedTs,
                                         string? PatternId = null, string? PatternName = null);

    public List<ApsLineStageRow> ListLineStages()
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            SELECT s.LineID, l.LineName, wc.ProcessCode, s.OffsetDays, s.UseStock, s.Note, s.PatternID, p.PatternName,
                   s.CreatedBy, s.CreatedTS, s.ModifiedBy, s.ModifiedTS
            FROM   dbo.MD_ApsLineStage s
            LEFT   JOIN dbo.MD_Line            l  ON l.LineID    = s.LineID
            LEFT   JOIN dbo.MD_WorkCenter      wc ON wc.WCID     = l.WCID
            LEFT   JOIN dbo.MD_LineTimePattern p  ON p.PatternID = s.PatternID
            ORDER  BY s.LineID;
            """, conn);
        using var rdr = cmd.ExecuteReader();
        var list = new List<ApsLineStageRow>();
        while (rdr.Read())
            list.Add(new((string)rdr["LineID"], rdr["LineName"] as string, TypeOf(rdr["ProcessCode"] as string ?? ""),
                         (int)rdr["OffsetDays"], (bool)rdr["UseStock"], rdr["Note"] as string,
                         (string)rdr["CreatedBy"], (DateTime)rdr["CreatedTS"], rdr["ModifiedBy"] as string, rdr["ModifiedTS"] as DateTime?,
                         rdr["PatternID"] as string, rdr["PatternName"] as string));
        return list;
    }

    /// <summary>있으면 UPDATE(ModifiedBy/TS), 없으면 INSERT(CreatedBy). 라인 콤보는 ListLines() 의 활성 INJ/IMG/PNT 라인. patternId 는 MD_LineTimePattern FK — 빈 값은 NULL(기본 패턴).</summary>
    public void SaveLineStage(string lineId, int offsetDays, bool useStock, string? note, string? patternId, string actor)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("""
            UPDATE dbo.MD_ApsLineStage
            SET    OffsetDays = @Off, UseStock = @Use, Note = @Note, PatternID = @Pat, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  LineID = @Line;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.MD_ApsLineStage (LineID, OffsetDays, UseStock, Note, PatternID, CreatedBy, CreatedTS)
                VALUES (@Line, @Off, @Use, @Note, @Pat, @By, SYSDATETIME());
            """, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value   = lineId;
        cmd.Parameters.Add("@Off",  SqlDbType.Int).Value           = offsetDays;
        cmd.Parameters.Add("@Use",  SqlDbType.Bit).Value           = useStock;
        cmd.Parameters.Add("@Note", SqlDbType.NVarChar, 200).Value = (object?)note ?? DBNull.Value;
        cmd.Parameters.Add("@Pat",  SqlDbType.VarChar, 20).Value   = (object?)(string.IsNullOrWhiteSpace(patternId) ? null : patternId.Trim()) ?? DBNull.Value;
        cmd.Parameters.Add("@By",   SqlDbType.NVarChar, 20).Value  = actor;
        cmd.ExecuteNonQuery();
    }

    public void DeleteLineStage(string lineId)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("DELETE FROM dbo.MD_ApsLineStage WHERE LineID = @Line;", conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        cmd.ExecuteNonQuery();
    }
}
