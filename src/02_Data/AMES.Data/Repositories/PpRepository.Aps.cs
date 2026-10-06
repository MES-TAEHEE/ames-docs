using System.Data;
using System.Text.Json;
using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Scheduling;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// PP-APS 「WO 생성」(스펙 §8 + 2026-10-05 BOM 규칙 확장) — 저장된 APS 실행의 사출 계획 행을 완제품 WO(FIFO SoID) + INJ 고정 슬롯(ShiftBands,
/// 사출 자식마다 1개) + 완제품 단계 슬롯(DeadlinePacker) 으로 바꾼다. 같은 품번 규칙 행은 자식 = 자기 자신, BOM 규칙 행은 저장본 BundleJson.Bom 의
/// 부모 완제품마다 공급 비율로 몫을 나눠 싣는다. PP-003 과 같은 NextWoSeq·ReleaseCore·MoldResolver·Append*Slot 을 한 트랜잭션 안에서 재사용한다.
/// </summary>
public sealed partial class PpRepository
{
    /// <summary>계획 행 거부 사유 — 고른 금형의 그 라인 MD_MoldLine.UPH 가 없거나 0(분 환산 불가). RejectNoMold 와 나란히.</summary>
    public const string RejectNoUph = "NoUph";

    public sealed record ApsWoOrder(string WoNumber, int WoId, int PlanLineId, string ItemNo, DateOnly PlanDate, int? SoId, decimal Qty,
                                    DateTime? Deadline, DateTime? DueDate,
                                    IReadOnlyList<DeadlinePacker.Placement> Placements,
                                    IReadOnlyList<DeadlinePacker.StepShortfall> Shortfalls,
                                    IReadOnlyList<DeadlinePacker.MoldChange> MoldChanges)
    {
        public decimal LateQty  => Placements.Where(p => p.Late).Sum(p => p.Qty);
        public decimal ShortQty => Shortfalls.Sum(s => s.Qty);
    }

    /// <summary>거부된 계획 행. Reason = RejectNoMold | RejectNoUph.</summary>
    public sealed record ApsRejectedLine(int PlanLineId, string ItemNo, DateOnly PlanDate, string Reason);

    public sealed record ApsWoResult(List<ApsWoOrder> Orders, List<ApsRejectedLine> Rejected, int SkippedExisting, int BomRuleExcluded, bool DryRun)
    {
        public int Created       => Orders.Count;
        public int Late          => Orders.Count(o => o.LateQty  > 0);
        public int Short         => Orders.Count(o => o.ShortQty > 0);
        public int RejectedCount => Rejected.Count;
    }

    /// <summary>CreateManualWo 의 INSERT 형태 + SoID(NULL 허용)·ProdDeadline·OUTPUT. 라우팅 없는 품목은 0행.</summary>
    internal const string InsertApsWoSql = """
        INSERT INTO dbo.PP_WorkOrder (WoNumber, SoID, ItemNo, OrderQty, OpenQty, DueDate, ProdDeadline, RoutingType, Status, CreatedBy, CreatedTS)
        OUTPUT INSERTED.WoID
        SELECT @Wo, @SoID, i.ItemNo, @Qty, @Qty, @Due, @Deadline, i.RoutingType, 'Draft', @Actor, SYSDATETIME()
        FROM   dbo.MD_Item i WHERE i.ItemNo = @ItemNo AND i.RoutingType IS NOT NULL;
        """;

    /// <summary>BOM 규칙 사출 행 거부 사유 — 저장본 BundleJson.Bom 에 이 자식을 쓰는 완제품 간선이 없다(실행 저장 뒤 BOM 이 바뀐 경우 등).</summary>
    public const string RejectNoParent = "NoParent";

    sealed record ApsInjLine(int PlanLineId, string ItemNo, string LineId, DateOnly PlanDate, decimal PlanDay, decimal PlanNight, int? WoId, bool SameItem);

    /// <summary>사출 자식 1개가 (부모, 날짜) WO 단위에 싣는 몫 — 부모 단위 주/야(기존 슬롯 차감 후), 자식 단위 = 부모 조각 × QtyPer(주/야는 DayRatio).</summary>
    sealed class ApsLeg
    {
        public required ApsInjLine Row { get; init; }           // 자식 사출 행(같은 품번 규칙이면 부모 자신의 행)
        public required string Line { get; init; }
        public required MoldResolver.MoldCandidate Mold { get; init; }
        public required decimal Uph { get; init; }
        public required double QtyPer { get; init; }
        public required decimal ParentDay { get; init; }        // 이 자식이 허용하는 부모 수량(주/야) — 기존 슬롯 차감 후
        public required decimal ParentNight { get; init; }
        public required ApsParentPlan Parent { get; init; }
        public decimal ParentTotal => ParentDay + ParentNight;
        /// <summary>자식 단위 총량 = WO 수량 × QtyPer. 야간은 이 자식 행의 야간 몫까지, 나머지는 주간(WO 수량이 이 자식 몫보다 크면 초과분은 주간).</summary>
        public decimal ChildTotal => Parent.Qty * (decimal)QtyPer;
        public decimal ChildNight => Math.Min(ParentNight * (decimal)QtyPer, ChildTotal);
        public decimal ChildDay   => ChildTotal - ChildNight;
        public int PlacedEnd { get; set; }                       // 이 자식 슬롯의 축 끝(완제품 단계 시작 기준)
        public List<decimal> PiecePlacedChild { get; } = new();  // 조각마다 실제로 실은 자식 수량
    }

    /// <summary>(완제품, 사출 계획일) WO 단위 — 같은 품번 규칙이면 자식 = 자기 자신 1개, BOM 규칙이면 자식마다 1개(스펙 2026-10-05).</summary>
    sealed class ApsParentPlan
    {
        public required string ItemNo { get; init; }
        public required DateOnly PlanDate { get; init; }
        public List<ApsLeg> Legs { get; } = new();
        public List<WorkOrderRepository.RoutingStepPreview> Template { get; set; } = new();
        public int InjStepSeq { get; set; }
        public int? FinStepSeq { get; set; }
        public string? FinLineId { get; set; }
        public DateTime Deadline { get; set; }
        public decimal Qty { get; set; }                         // 완제품 단위 = 자식 몫 중 최대(내림)
        public List<(int WoId, string Wo, ApsWoAllocator.Piece Piece, DateTime Due)> Pieces { get; } = new();
        public List<List<DeadlinePacker.Placement>> PiecePlacements { get; } = new();
        public List<List<DeadlinePacker.StepShortfall>> PieceShortfalls { get; } = new();
        public List<DeadlinePacker.MoldChange> FirstPieceMc { get; } = new();
        public ApsLeg FirstLeg => Legs.OrderBy(l => l.Row.ItemNo, StringComparer.Ordinal).First();
        public int InjEnd => Legs.Max(l => l.PlacedEnd);
    }

    /// <summary>
    /// 스펙 §8 + 2026-10-05 BOM 규칙 확장. 한 트랜잭션. Released 실행이면 InvalidOperationException(화면은 모달 오류).
    /// dryRun=true 는 같은 경로를 돌고 Rollback(미리보기, WoNumber 는 잠정).
    /// </summary>
    public ApsWoResult CreateApsWorkOrders(int runId, IReadOnlyList<int> planLineIds, string actor, bool dryRun = false)
    {
        var orders   = new List<ApsWoOrder>();
        var rejected = new List<ApsRejectedLine>();
        int skipped = 0;
        var wanted = planLineIds.ToHashSet();

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var status = ReadApsRunStatus(conn, tx, runId)
                         ?? throw new InvalidOperationException($"APS run #{runId} not found.");
            if (string.Equals(status, ApsRepository.StatusReleased, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"APS run #{runId} is already released.");

            var now     = ReadNow(conn, tx);
            var prefix  = $"WO-{now:yyyyMMdd}-";
            var allInj  = ReadApsInjLines(conn, tx, runId);
            var asmLine = ReadApsAsmLines(conn, tx, runId);
            var asmSupply = ReadApsAsmSupply(conn, tx, runId);
            // 부모 ↔ 사출 자식 간선은 저장본(BundleJson.Bom, 재현 정본)에서 — 자기 간선(같은 품번 규칙)은 제외
            var parentsOf = ReadBundleBom(conn, tx, runId)
                .Where(e => !string.Equals(e.ParentPartNo, e.ChildPartNo, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.ChildPartNo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.ParentPartNo, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

            var targets = new List<ApsInjLine>();
            foreach (var r in allInj)
            {
                if (!wanted.Contains(r.PlanLineId) || r.PlanDay + r.PlanNight <= 0) continue;
                if (r.WoId is not null) { skipped++; continue; }
                targets.Add(r);
            }

            // 완제품(WO 품번) = 같은 품번 규칙 행은 자기 자신, BOM 규칙 행은 간선의 부모들
            var parentItems = targets.SelectMany(t => t.SameItem ? new[] { t.ItemNo }
                                                    : parentsOf.GetValueOrDefault(t.ItemNo)?.Select(e => e.ParentPartNo) ?? Enumerable.Empty<string>())
                                     .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var openOrders = ReadOpenOrders(conn, tx, parentItems);
            var first  = targets.Count == 0 ? now.Date : targets.Min(t => t.PlanDate).ToDateTime(TimeOnly.MinValue);
            var calEnd = targets.Count == 0 ? now.Date : targets.Max(t => t.PlanDate).ToDateTime(TimeOnly.MinValue).AddDays(75);
            // 완제품 단계 탐색 상한은 수주 납기까지 간다 — 그 범위의 휴무일도 읽어 둔다(PP-003 과 같은 규칙)
            foreach (var o in openOrders)
                if (o.DueDate is { } due && due.ToDateTime(TimeOnly.MinValue) >= calEnd) calEnd = due.ToDateTime(TimeOnly.MinValue).AddDays(1);
            var cal      = new WorkdayCalendar(ReadCalendar(conn, tx, first.AddDays(-14), calEnd));
            var dailyCap = ReadDailyCap(conn, tx);
            var parsed   = ApsRepository.ReadSettings(conn, tx);
            var settings = parsed.Settings;
            // 모든 라인의 능력은 APS 설정 패턴(라인 지정 → DEFAULT_PATTERN)으로 읽는다(2026-10-06, BuildBundle 과 같은 규칙). 사출(대상 행) 라인에 패턴이 없으면
            // 쓰기 전에 전부 거부한다. 완제품 단계(IMG/PNT) 라인은 둘 다 없을 때만 종전 자동 해석(PP_LineSchedule → 라인 전용 → 전역)으로 떨어진다 —
            // 사용자 지적 "라인에 설정 안 했으면 기본 패턴을 따라야"(전에는 완제품 라인이 기본 패턴을 건너뛰어 빈 전역 패턴 LP-CMN-001 을 잡았다)
            var patterns = new ApsPatternResolver(parsed, ApsRepository.ReadPatternStatus(conn, tx, parsed));
            foreach (var line in targets.Select(t => t.LineId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(l => l, StringComparer.Ordinal))
                patterns.Resolve(line);
            if (patterns.Problems.Count > 0) throw new ApsConfigurationException(patterns.Problems);
            string? PatternFor(string line)
            {
                if (targets.Any(t => string.Equals(t.LineId, line, StringComparison.OrdinalIgnoreCase))) return patterns.Resolve(line);
                var pat = parsed.EffectivePattern(line);
                if (pat is null) return null;                                   // 완제품 라인에 아무 설정도 없음 → 자동 해석
                return patterns.Resolve(line) ?? throw new ApsConfigurationException(patterns.Problems);   // 지정은 있는데 없는/비활성 패턴 → 거부
            }
            var days = new DeadlinePacker.DayStateCache(
                (line, date) => LineScheduleRepository.ReadDayCapacity(conn, tx, line, date, PatternFor(line)),
                (line, date) => LineScheduleRepository.LineLastMoldBefore(conn, tx, line, date));
            var routing   = ReadItemRouting(conn, tx, parentItems);
            var packOf    = ReadItemBoxQty(conn, tx, parentItems);   // BOM 규칙 WO 수량 올림 단위 = 완제품 BoxQty ?? APS_SETTING.ROUND_TO(10-06 사용자 요청)
            var allocator = new ApsWoAllocator(openOrders);

            // 1단계: 사출 행마다 금형·UPH 판정 → (부모, 날짜) 단위에 자식 몫을 싣는다. 거부는 여기서 끝나고 WO 는 만들지 않는다
            var plans   = new Dictionary<(string Item, DateOnly Date), ApsParentPlan>();
            var covered = new HashSet<int>();   // 그 날 기존 슬롯이 수량을 다 덮은 행(스펙 §8.1 등록 계획분 차감)
            var rowLegs = new Dictionary<int, List<ApsLeg>>();
            foreach (var r in targets.OrderBy(t => t.PlanDate).ThenBy(t => t.ItemNo, StringComparer.Ordinal))
            {
                var date = r.PlanDate.ToDateTime(TimeOnly.MinValue);
                var mold = MoldResolver.Choose(MasterDataRepository.ReadMoldCandidates(conn, tx, r.ItemNo, r.LineId),
                                               days.LastMoldId(r.LineId, date));
                if (mold is null) { rejected.Add(new ApsRejectedLine(r.PlanLineId, r.ItemNo, r.PlanDate, RejectNoMold)); continue; }
                var uph = ReadMoldLineUph(conn, tx, mold.MoldId, r.LineId);
                if (uph is not > 0) { rejected.Add(new ApsRejectedLine(r.PlanLineId, r.ItemNo, r.PlanDate, RejectNoUph)); continue; }

                // 계획 행에는 등록 계획(기존 WO 슬롯)이 잠긴 칸으로 합산돼 있다(§4.5) — 같은 규칙(슬롯 StartMin 의 교대)으로 주/야에서 뺀다
                var cap0 = days.Get(r.LineId, date);
                var full = ShiftBands.Split(ShiftBandsOf(cap0), Array.Empty<SlotPacker.Interval>(), cap0.DayStart);
                var (exDayC, exNightC) = ReadScheduledQty(conn, tx, r.LineId, date, r.ItemNo, full);
                var (dayC, nightC) = SubtractScheduled(r.PlanDay, r.PlanNight, exDayC, exNightC);   // 자식 품번 슬롯은 모든 부모 몫에서 먼저 뺀다
                if (dayC + nightC <= 0) { skipped++; covered.Add(r.PlanLineId); continue; }

                // 부모별 몫(스펙 2026-10-05 §1): 같은 품번 규칙 = 자기 자신 100%, BOM 규칙 = 부모 공급(d + 선행일) × QtyPer 비율(전부 0 이면 균등)
                List<(string Parent, double QtyPer, decimal Weight)> shares;
                if (r.SameItem) shares = new() { (r.ItemNo, 1d, 1m) };
                else
                {
                    if (parentsOf.GetValueOrDefault(r.ItemNo) is not { Count: > 0 } edges)
                    { rejected.Add(new ApsRejectedLine(r.PlanLineId, r.ItemNo, r.PlanDate, RejectNoParent)); continue; }
                    var supplyDate = DateOnly.FromDateTime(AddWorkdays(cal, date, InjOffsetDays(settings, r.LineId)));
                    shares = edges.Select(e => (e.ParentPartNo, e.QtyPer, asmSupply.GetValueOrDefault((e.ParentPartNo.ToUpperInvariant(), supplyDate)) * (decimal)e.QtyPer)).ToList();
                    if (shares.All(x => x.Weight <= 0)) shares = shares.Select(x => (x.Parent, x.QtyPer, 1m)).ToList();
                }
                decimal weightSum = shares.Sum(x => x.Weight);
                var legs = new List<ApsLeg>();
                foreach (var (parent, qtyPer, weight) in shares)
                {
                    if (weight <= 0) continue;
                    decimal shareDay   = dayC   * weight / weightSum / (decimal)qtyPer;   // 부모 단위
                    decimal shareNight = nightC * weight / weightSum / (decimal)qtyPer;
                    var (exDayP, exNightP) = r.SameItem ? (0m, 0m) : ReadScheduledQty(conn, tx, r.LineId, date, parent, full);   // 부모 품번 슬롯(데모 WO)
                    var (pDay, pNight) = SubtractScheduled(shareDay, shareNight, exDayP, exNightP);
                    if (pDay + pNight <= 0) continue;
                    if (!plans.TryGetValue((parent.ToUpperInvariant(), r.PlanDate), out var plan))
                        plans[(parent.ToUpperInvariant(), r.PlanDate)] = plan = new ApsParentPlan { ItemNo = parent, PlanDate = r.PlanDate };
                    var leg = new ApsLeg { Row = r, Line = r.LineId, Mold = mold, Uph = uph.Value, QtyPer = qtyPer, ParentDay = pDay, ParentNight = pNight, Parent = plan };
                    plan.Legs.Add(leg); legs.Add(leg);
                }
                if (legs.Count == 0) { skipped++; covered.Add(r.PlanLineId); continue; }
                rowLegs[r.PlanLineId] = legs;
            }

            // 부모 수량·라우팅·마감(스펙 2026-10-05 §2) — 0 이 된 부모는 버리고, 그 부모만 쓰던 자식 행은 덮인 것으로.
            // BOM 규칙(부모와 다른 자식이 하나라도 있으면)은 비율 분배로 생긴 자투리를 완제품 포장 단위로 올린다(ApsRepository 의 완제품 PackRule 과 같은 BoxQty ?? RoundTo);
            // 같은 품번 규칙만이면 엔진이 이미 자식 포장 단위로 올린 값이라 종전대로 내림만
            foreach (var plan in plans.Values.ToList())
            {
                decimal raw = plan.Legs.Max(l => l.ParentTotal);
                bool bomRule = plan.Legs.Any(l => !string.Equals(l.Row.ItemNo, plan.ItemNo, StringComparison.OrdinalIgnoreCase));
                plan.Qty = !bomRule || raw <= 0 ? Math.Floor(raw)
                         : RoundUpToPack(raw, packOf.GetValueOrDefault(plan.ItemNo) is int bq && bq > 0 ? bq : settings.RoundTo);
                if (plan.Qty <= 0) { plans.Remove((plan.ItemNo.ToUpperInvariant(), plan.PlanDate)); continue; }
                if (routing.GetValueOrDefault(plan.ItemNo) is not { } rt)
                    throw new InvalidOperationException($"{plan.ItemNo}: RoutingType missing.");
                plan.Template = WorkOrderRepository.ReadPreview(conn, tx, plan.ItemNo, rt);
                var injStep = plan.Template.FirstOrDefault(t => MoldResolver.NeedsMold(t.ProcessCode))
                              ?? throw new InvalidOperationException($"{plan.ItemNo}: routing {rt} has no {MoldResolver.MoldProcessCode} step.");
                plan.InjStepSeq = injStep.StepSeq;
                var finSeq  = plan.Template.Where(t => t.LineRequired).Select(t => (int?)t.StepSeq).Max();
                var finStep = finSeq is int fs && fs != injStep.StepSeq ? plan.Template.First(t => t.StepSeq == fs) : null;
                plan.FinStepSeq = finStep?.StepSeq;
                plan.FinLineId  = finStep is null ? null
                                : asmLine.GetValueOrDefault(plan.ItemNo) ?? finStep.BopLineId
                                  ?? (finStep.Candidates.Count == 1 ? finStep.Candidates[0].LineId : null);
                plan.Deadline = AddWorkdays(cal, plan.PlanDate.ToDateTime(TimeOnly.MinValue), InjOffsetDays(settings, plan.FirstLeg.Line));
            }
            foreach (var (rowId, legs) in rowLegs)
                if (legs.All(l => !plans.ContainsKey((l.Parent.ItemNo.ToUpperInvariant(), l.Parent.PlanDate)))) { skipped++; covered.Add(rowId); }
            var liveLegs = plans.Values.SelectMany(p => p.Legs).ToList();

            // 2단계: WO INSERT + Release — 부모·날짜 순. MC 의 RefID 로 대표 첫 WO 가 필요해 슬롯보다 먼저 전부 만든다
            var seq = NextWoSeq(conn, tx, prefix);
            using var ins = BuildInsertApsWo(conn, tx, actor);
            foreach (var plan in plans.Values.OrderBy(p => p.PlanDate).ThenBy(p => p.ItemNo, StringComparer.Ordinal))
            {
                var firstLine = plan.FirstLeg.Line;
                foreach (var piece in allocator.Allocate(plan.ItemNo, plan.Qty))
                {
                    var wo  = $"{prefix}{(seq + 1):D3}";
                    var due = piece.DueDate?.ToDateTime(TimeOnly.MinValue) ?? plan.Deadline;
                    int woId = ExecInsertApsWo(ins, wo, piece.SoId, plan.ItemNo, piece.Qty, due, plan.Deadline);
                    seq++;
                    var choices = plan.Template.Select(t => new WorkOrderRepository.StepLineChoice(t.StepSeq, LineFor(plan, firstLine, t))).ToList();
                    if (WorkOrderRepository.ReleaseCore(conn, tx, woId, choices, actor) == 0)
                        throw new InvalidOperationException($"{wo}: release failed.");
                    plan.Pieces.Add((woId, wo, piece, due));
                    plan.PiecePlacements.Add(new()); plan.PieceShortfalls.Add(new());
                }
                foreach (var leg in plan.Legs) foreach (var _ in plan.Pieces) leg.PiecePlacedChild.Add(0);
            }
            // 자식 행 ↔ 첫 WO: 품번순 첫 부모의 첫 조각 (PP_ApsPlanLine.WoID)
            var createdIds = new HashSet<int>();
            foreach (var (rowId, legs) in rowLegs)
            {
                var firstParent = legs.Where(l => l.Parent.Pieces.Count > 0).OrderBy(l => l.Parent.ItemNo, StringComparer.Ordinal).FirstOrDefault();
                if (firstParent is null) continue;
                UpdatePlanLineWo(conn, tx, rowId, firstParent.Parent.Pieces[0].WoId);
                createdIds.Add(rowId);
            }

            // 3단계: (계획일, 사출 라인, 금형, 색상) 그룹 — 같은 그룹 = 형제(한 shot), 대표(자식 품번 → 부모 품번 Ordinal 첫)가 시간을 점유.
            // 같은 금형의 다른 색상은 따로 찍으므로 형제가 아니다(ApsRepository 의 패밀리 규칙과 같다)
            foreach (var g in liveLegs.GroupBy(l => (l.Parent.PlanDate, l.Line, l.Mold.MoldId, Color: l.Mold.Color ?? ""))
                                      .OrderBy(g => g.Key.PlanDate)
                                      .ThenBy(g => g.Key.Line, StringComparer.Ordinal)
                                      .ThenBy(g => g.Key.MoldId, StringComparer.Ordinal)
                                      .ThenBy(g => g.Key.Color, StringComparer.Ordinal))
            {
                var members = g.OrderBy(l => l.Row.ItemNo, StringComparer.Ordinal).ThenBy(l => l.Parent.ItemNo, StringComparer.Ordinal).ToList();
                // 형제 = 한 shot 에 같이 나오는 **다른 자식 품번**. 같은 자식 품번이 여러 부모 WO 로 나뉜 조각은 같은 금형이 차례로 찍는 양이라 **합산**한다
                // (사용자 결정 2026-10-06 — 전에는 조각마다 형제로 보아 대표 1건만 시간을 받고 나머지는 0분 행, 사출 부하가 조각 수만큼 과소).
                // 그룹 시간 = 품번별 합계 중 최대(품번 1개면 그 합계), 대표 품번 = ItemNo Ordinal 첫 품번(ShiftBands.Representative 와 같은 규칙)
                var byItem   = members.GroupBy(m => m.Row.ItemNo, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
                var repLegs  = byItem[0].ToList();                           // 대표 품번의 조각들(부모 Ordinal 순) — 전부 실제 슬롯
                var rep      = repLegs[0];
                var lastRep  = repLegs[^1];
                var date    = g.Key.PlanDate.ToDateTime(TimeOnly.MinValue);
                var line    = g.Key.Line;
                var moldId  = g.Key.MoldId;
                decimal uph = rep.Uph;                                       // 같은 (금형, 라인) → 형제도 같은 값
                int dayWant   = MinutesOrZero(byItem.Max(x => x.Sum(m => m.ChildDay)),   uph);
                int nightWant = MinutesOrZero(byItem.Max(x => x.Sum(m => m.ChildNight)), uph);
                int repFirstWo = rep.Parent.Pieces[0].WoId;

                // ② 대표 시간 — 주간·야간 잔여 구간 앞에서부터 따로(형제 중 최대 수량 기준). 모자란 교대를 다른 교대로 넘기지 않는다
                var cap   = days.Get(line, date);
                var bands = ShiftBands.Split(ShiftBandsOf(cap), cap.Occupied, cap.DayStart);
                var dayBands   = bands.Day;
                var nightBands = bands.Night;

                // ③ 금형 교체 — 직전 금형(그 날 앞 슬롯 → 이전 날짜 → 장착 금형)과 다르면 MC 를 생산 바로 앞에 둔다(스펙 §8.2).
                //    창 순서 = 주간(주간 수량이 있을 때) → 야간(야간 수량이 있을 때). 창마다 교체분 + 1분이 한 빈틈에 들어가는 첫 자리를 찾아
                //    앞 교체분을 MC 로, 그 교대 생산은 MC 끝부터(같은 빈틈에서 바로 이어진다). MC 가 들어간 창보다 앞 창은 금형이 안 바뀌었으니 생산 없음(Shortfall).
                //    잘못된 금형으로 생산하지 않는다 — 어느 창에도 교체분 + 1분(교체 뒤 생산 1분 이상)이 한 빈틈에 들어가지 않으면
                //    (교체분조차 없는 경우 포함) MC 도 생산도 쓰지 않고 그 날 수량 전량 Shortfall(WO 는 Released 로 PP-LSB 미배치 목록)
                var prevMold = days.LastMoldId(line, date);
                if (!string.Equals(prevMold, moldId, StringComparison.OrdinalIgnoreCase) && rep.Mold.ChangeMin > 0)
                {
                    int change = rep.Mold.ChangeMin;
                    bool placed = false;
                    foreach (bool isDay in new[] { true, false })
                    {
                        if ((isDay ? dayWant : nightWant) <= 0) continue;
                        var window = isDay ? dayBands : nightBands;
                        if (SlotPacker.Place(window, Array.Empty<SlotPacker.Interval>(), change + 1, cap.DayStart) is { } gap)
                        {
                            var mc = new SlotPacker.Interval(gap.StartMin, gap.StartMin + change);
                            LineScheduleRepository.AppendMoldChangeSlot(conn, tx, line, date, cap.PatternId, repFirstWo, prevMold, moldId, mc.StartMin, mc.EndMin, actor);
                            days.Occupy(line, date, mc, moldId);
                            rep.Parent.FirstPieceMc.Add(new DeadlinePacker.MoldChange(rep.Parent.InjStepSeq, line, date, mc.StartMin, mc.EndMin, prevMold, moldId, false));
                            if (isDay) dayBands = From(dayBands, mc, cap.DayStart);
                            else { dayBands = Array.Empty<SlotPacker.Interval>(); nightBands = From(nightBands, mc, cap.DayStart); }
                            placed = true;
                            break;
                        }
                    }
                    if (!placed)
                        (dayBands, nightBands) = (Array.Empty<SlotPacker.Interval>(), Array.Empty<SlotPacker.Interval>());
                }

                var dayAlloc   = ShiftBands.Allocate(dayBands,   dayWant,   cap.DayStart);
                var nightAlloc = ShiftBands.Allocate(nightBands, nightWant, cap.DayStart);
                var allocated  = dayAlloc.Slots.Concat(nightAlloc.Slots).ToList();
                foreach (var s in allocated) days.Occupy(line, date, s, moldId);
                int dayStart = cap.DayStart;
                int injEnd   = allocated.Count > 0 ? allocated.MaxBy(s => AxisEnd(s, dayStart)).EndMin : cap.LastWoEnd ?? cap.DayStart;
                var dayPool   = dayAlloc.Slots.ToList();
                var nightPool = nightAlloc.Slots.ToList();

                // ④ 회원(자식 몫)별: 부모 조각을 자식 단위(× QtyPer, 주/야 비율)로 바꿔 싣는다. 대표 품번의 조각 = 교대 구간을 차례로 잘라 실제 슬롯(마지막 조각은
                //    형제 몫까지 남은 구간 전부 — 수량 0 구간도 써서 점유를 DB 에 남긴다), 형제 품번의 조각 = 대표 교대 첫 슬롯 시작의 0분 슬롯 → PP_ApsRunWo.
                //    형제 품번이 그 교대에 받은 커버 수량은 품번 단위로 조각들이 나눠 쓴다
                var dayCovByItem   = byItem.Skip(1).ToDictionary(x => x.Key, x => Covered(dayAlloc,   x.Sum(m => m.ChildDay),   uph), StringComparer.OrdinalIgnoreCase);
                var nightCovByItem = byItem.Skip(1).ToDictionary(x => x.Key, x => Covered(nightAlloc, x.Sum(m => m.ChildNight), uph), StringComparer.OrdinalIgnoreCase);
                foreach (var m in members)
                {
                    bool isRep = string.Equals(m.Row.ItemNo, rep.Row.ItemNo, StringComparison.OrdinalIgnoreCase);
                    var plan   = m.Parent;
                    m.PlacedEnd = injEnd;
                    decimal dayLeft  = m.ChildDay;                           // 조각을 주간 몫부터 채워 주/야로 나눈다(같은 품번 규칙과 같은 순서)
                    for (int i = 0; i < plan.Pieces.Count; i++)
                    {
                        var (woId, _, piece, _) = plan.Pieces[i];
                        decimal childQty  = piece.Qty * (decimal)m.QtyPer;
                        decimal dayPart   = Math.Min(childQty, dayLeft);
                        decimal nightPart = childQty - dayPart;
                        dayLeft -= dayPart;
                        var placements = plan.PiecePlacements[i];
                        decimal placedQty = 0;
                        if (isRep)
                        {
                            bool lastPiece = ReferenceEquals(m, lastRep) && i == plan.Pieces.Count - 1;   // 대표 품번의 마지막 조각만 남은 구간 전부
                            foreach (var (pool, part) in new[] { (dayPool, dayPart), (nightPool, nightPart) })
                            {
                                int want  = MinutesOrZero(part, uph);
                                var taken = Carve(pool, lastPiece ? int.MaxValue : want);
                                bool fullyServed = taken.Sum(s => s.EndMin - s.StartMin) >= want;
                                foreach (var (slot, q) in Spread(taken, part, uph, fullyServed))
                                {
                                    LineScheduleRepository.AppendWoSlot(conn, tx, line, date, cap.PatternId, woId, slot.StartMin, slot.EndMin, q, moldId, actor);
                                    placements.Add(new DeadlinePacker.Placement(plan.InjStepSeq, line, date, slot.StartMin, slot.EndMin, q, false, moldId));
                                    placedQty += q;
                                }
                            }
                        }
                        else
                        {
                            // 형제 품번: 한 shot 이 함께 찍는다 — 대표가 그 교대에 받은 시간만큼만 싣고(수량만 기록, MoldID 는 대표 슬롯에만) 나머지는 Shortfall
                            decimal d = Math.Min(dayPart,   dayCovByItem[m.Row.ItemNo]);   dayCovByItem[m.Row.ItemNo]   -= d;
                            decimal n = Math.Min(nightPart, nightCovByItem[m.Row.ItemNo]); nightCovByItem[m.Row.ItemNo] -= n;
                            foreach (var (qty, at) in new[] { (d, dayAlloc.Slots), (n, nightAlloc.Slots) })
                            {
                                if (qty <= 0) continue;
                                int start = at[0].StartMin;
                                LineScheduleRepository.AppendWoSlot(conn, tx, line, date, cap.PatternId, woId, start, start, qty, null, actor);
                                placements.Add(new DeadlinePacker.Placement(plan.InjStepSeq, line, date, start, start, qty, false, null));
                                placedQty += qty;
                            }
                        }
                        if (childQty - placedQty > 0)
                            plan.PieceShortfalls[i].Add(new DeadlinePacker.StepShortfall(plan.InjStepSeq, line, childQty - placedQty));
                        m.PiecePlacedChild[i] = placedQty;
                        InsertApsRunWo(conn, tx, runId, m.Row.PlanLineId, woId, piece.SoId, childQty, actor);
                    }
                }
            }

            // 4단계: 완제품 단계(DeadlinePacker) — 조각마다, 사출이 실제로 실은 몫까지만(모든 자식의 실적 ÷ QtyPer 의 최소; PP-003 과 같은 원칙).
            //    못 실은 나머지는 그 단계 Shortfall — 사출이 0 이면 완제품 슬롯도 쓰지 않는다
            foreach (var plan in plans.Values.OrderBy(p => p.PlanDate).ThenBy(p => p.ItemNo, StringComparer.Ordinal))
            {
                var date = plan.PlanDate.ToDateTime(TimeOnly.MinValue);
                for (int i = 0; i < plan.Pieces.Count; i++)
                {
                    var (woId, wo, piece, due) = plan.Pieces[i];
                    var placements = plan.PiecePlacements[i];
                    var shortfalls = plan.PieceShortfalls[i];
                    if (plan.FinStepSeq is int fseq && plan.FinLineId is { } fline)
                    {
                        decimal placedQty = Math.Floor(plan.Legs.Min(l => l.PiecePlacedChild[i] / (decimal)l.QtyPer));
                        decimal finPlaced = 0;
                        if (placedQty > 0)
                        {
                            var demand = new DeadlinePacker.StepDemand(fseq, fline, placedQty,
                                plan.Template.First(t => t.StepSeq == fseq).StdCycleSec, dailyCap.GetValueOrDefault(fline));
                            var packed = DeadlinePacker.Pack(new[] { demand }, date, plan.InjEnd, plan.Deadline, due, cal, days);
                            foreach (var p in packed.Placements)
                                LineScheduleRepository.AppendWoSlot(conn, tx, p.LineId, p.Date, days.Get(p.LineId, p.Date).PatternId,
                                                                    woId, p.StartMin, p.EndMin, p.Qty, null, actor);
                            placements.AddRange(packed.Placements);
                            finPlaced = packed.Placements.Sum(p => p.Qty);
                        }
                        if (piece.Qty - finPlaced > 0)
                            shortfalls.Add(new DeadlinePacker.StepShortfall(fseq, fline, piece.Qty - finPlaced));
                    }
                    orders.Add(new ApsWoOrder(wo, woId, plan.FirstLeg.Row.PlanLineId, plan.ItemNo, plan.PlanDate, piece.SoId, piece.Qty,
                                              plan.Deadline, due, placements, shortfalls,
                                              i == 0 ? plan.FirstPieceMc : Array.Empty<DeadlinePacker.MoldChange>()));
                }
            }

            // Released = 대상이 될 수 있는 행(수량>0·WoID NULL)이 남지 않았고 WO 가 연결된 행이 하나라도 있을 때.
            // 거부(NoMold/NoUph/NoParent)·미선택 행은 남긴다 — 금형 등록 후 같은 실행으로 재실행할 수 있게.
            bool remaining = allInj.Any(r => r.PlanDay + r.PlanNight > 0 && r.WoId is null
                                             && !createdIds.Contains(r.PlanLineId) && !covered.Contains(r.PlanLineId));
            bool anyLinked = createdIds.Count > 0 || allInj.Any(r => r.WoId is not null);
            if (!remaining && anyLinked) MarkRunReleased(conn, tx, runId, actor);

            if (dryRun) tx.Rollback(); else tx.Commit();
            return new ApsWoResult(orders, rejected, skipped, 0, dryRun);
        }
        catch { tx.Rollback(); throw; }
    }

    // ── 순수 헬퍼 ────────────────────────────────────────────────────────

    /// <summary>단계 라인 — INJ = 품번순 첫 자식의 사출 라인, 라인 있는 마지막 단계 = 완제품 라인, 그 밖의 LineRequired = BOP 라인 → 후보 1개, 나머지 NULL.</summary>
    static string? LineFor(ApsParentPlan m, string injLine, WorkOrderRepository.RoutingStepPreview t)
    {
        if (t.StepSeq == m.InjStepSeq) return injLine;
        if (t.StepSeq == m.FinStepSeq) return m.FinLineId;
        if (!t.LineRequired) return null;
        return t.BopLineId ?? (t.Candidates.Count == 1 ? t.Candidates[0].LineId : null);
    }

    /// <summary>ApsRepository.BuildBundle 과 같은 해석 — 교대 정보가 없으면 빈 목록(비가동일: 주간으로 보지 않고 전량 Shortfall).</summary>
    static IReadOnlyList<(SlotPacker.Interval Band, int ShiftSort)> ShiftBandsOf(LineScheduleRepository.DayCapacity cap)
        => cap.ShiftBands ?? Array.Empty<(SlotPacker.Interval, int)>();

    static int MinutesOrZero(decimal qty, decimal uph) => qty <= 0 ? 0 : ShiftBands.MinutesFor(qty, uph);

    /// <summary>포장 단위 올림 — pack 이 1 이하면 정수 올림. 0 이하 수량은 0.</summary>
    internal static decimal RoundUpToPack(decimal qty, int pack)
    {
        if (qty <= 0) return 0;
        int p = Math.Max(1, pack);
        return Math.Ceiling(qty / p) * p;
    }

    /// <summary>
    /// 계획 주/야 − 기존 슬롯 주/야. 한 교대가 음수(기존 슬롯이 계획보다 많음)면 그 초과분을 다른 교대에서 뺀다 —
    /// 발행 수량은 (계획 합 − 기존 합) 을 넘지 않는다(주 50·야 60, 기존 주 80 → 주 0·야 30).
    /// </summary>
    internal static (decimal Day, decimal Night) SubtractScheduled(decimal planDay, decimal planNight, decimal exDay, decimal exNight)
    {
        decimal day = planDay - exDay, night = planNight - exNight;
        if (day   < 0) { night += day; day   = 0; }
        if (night < 0) { day   += night; night = 0; }
        return (Math.Max(0m, day), Math.Max(0m, night));
    }

    /// <summary>축(dayStart 기준)에서 after 가 끝난 뒤 부분만 남긴다 — MC 뒤 생산 구간. 끝을 AxisEnd 로 재어 축 끝(자정 넘김)의 MC 도 0 으로 감기지 않는다.</summary>
    static List<SlotPacker.Interval> From(IReadOnlyList<SlotPacker.Interval> bands, SlotPacker.Interval after, int dayStart)
    {
        int Axis(int m) { int r = (m - dayStart) % 1440; return r < 0 ? r + 1440 : r; }
        int floor = AxisEnd(after, dayStart);
        var list = new List<SlotPacker.Interval>();
        foreach (var b in bands)
        {
            int lo = Axis(b.StartMin), hi = lo + (b.EndMin - b.StartMin);
            if (hi <= floor) continue;
            list.Add(lo >= floor ? b : new SlotPacker.Interval(b.StartMin + (floor - lo), b.EndMin));
        }
        return list;
    }

    /// <summary>형제 수량 중 대표가 그 교대에 받은 시간으로 찍히는 몫 — 다 받았으면 전부, 모자라면 받은 분 × UPH 내림.</summary>
    static decimal Covered(ShiftBands.Allocation a, decimal qty, decimal uph)
        => a.ShortMin == 0 ? qty : Math.Min(qty, Math.Floor(a.PlacedMin * uph / 60m));

    /// <summary>축(dayStart 기준) 상의 끝 — 자정을 넘긴 야간 구간이 앞 구간보다 뒤로 오게(SlotPacker 와 같은 규칙).</summary>
    static int AxisEnd(SlotPacker.Interval s, int dayStart)
    {
        int r = (s.StartMin - dayStart) % 1440;
        return (r < 0 ? r + 1440 : r) + (s.EndMin - s.StartMin);
    }

    /// <summary>사출 라인 선행일 — MD_ApsLineStage 공통 행(RootLine 없음) → APS_SETTING.INJ_OFFSET_DAYS(StageDefaults.Injection).</summary>
    static int InjOffsetDays(Settings s, string lineId)
    {
        foreach (var st in s.LineStages)
            if (string.IsNullOrEmpty(st.RootLine) && string.Equals(st.LineCd, lineId, StringComparison.OrdinalIgnoreCase))
                return st.OffsetDays;
        return s.StageDefaults.Injection.OffsetDays;
    }

    /// <summary>WorkdayCalendar 에는 AddWorkdays 가 없다 — from 은 세지 않고 근무일 n 개 뒤.</summary>
    static DateTime AddWorkdays(WorkdayCalendar cal, DateTime from, int n)
    {
        var d = from.Date;
        while (n > 0) { d = d.AddDays(1); if (cal.IsWorkday(d)) n--; }
        return d;
    }

    /// <summary>pool 앞에서 want 분을 떼어 낸다(마지막 구간은 잘라 나머지를 pool 에 남긴다). want = int.MaxValue 면 전부.</summary>
    static List<SlotPacker.Interval> Carve(List<SlotPacker.Interval> pool, int want)
    {
        var taken = new List<SlotPacker.Interval>();
        while (want > 0 && pool.Count > 0)
        {
            var s = pool[0];
            int len = s.EndMin - s.StartMin;
            if (len <= want) { taken.Add(s); pool.RemoveAt(0); want -= len; }
            else
            {
                taken.Add(new SlotPacker.Interval(s.StartMin, s.StartMin + want));
                pool[0] = new SlotPacker.Interval(s.StartMin + want, s.EndMin);
                want = 0;
            }
        }
        return taken;
    }

    /// <summary>
    /// 조각 수량을 떼어 낸 구간 전부에 나눠 싣는다 — 앞 구간부터 구간 길이 × UPH(내림)만큼, fullyServed 면 마지막 구간이 남은 수량 전부(분 올림 오차 흡수).
    /// 수량이 먼저 바닥나도 구간은 수량 0 으로 돌려준다 — 형제 몫으로 점유한 시간이 DB 에 남아야 능력 계산이 맞는다.
    /// </summary>
    static List<(SlotPacker.Interval Slot, decimal Qty)> Spread(List<SlotPacker.Interval> slots, decimal qty, decimal uph, bool fullyServed)
    {
        var list = new List<(SlotPacker.Interval, decimal)>(slots.Count);
        decimal left = qty;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            decimal q = fullyServed && i == slots.Count - 1 ? left : Math.Min(left, Math.Floor((s.EndMin - s.StartMin) * uph / 60m));
            list.Add((s, q));
            left -= q;
        }
        return list;
    }

    // ── DB 헬퍼 (호출자 트랜잭션 안) ─────────────────────────────────────

    /// <summary>실행 행을 잠그고 상태를 읽는다 — 같은 실행의 동시 「WO 생성」을 직렬화한다. 없으면 null.</summary>
    static string? ReadApsRunStatus(SqlConnection conn, SqlTransaction tx, int runId)
    {
        using var cmd = new SqlCommand("SELECT Status FROM dbo.PP_ApsRun WITH (UPDLOCK, ROWLOCK) WHERE RunID = @R;", conn, tx);
        cmd.Parameters.Add("@R", SqlDbType.Int).Value = runId;
        return cmd.ExecuteScalar() as string;
    }

    static List<ApsInjLine> ReadApsInjLines(SqlConnection conn, SqlTransaction tx, int runId)
    {
        using var cmd = new SqlCommand("""
            SELECT PlanLineID, ItemNo, LineID, PlanDate, PlanDay, PlanNight, WoID, SameItem
            FROM   dbo.PP_ApsPlanLine WITH (UPDLOCK, ROWLOCK)
            WHERE  RunID = @R AND Kind = @K
            ORDER  BY PlanDate, ItemNo;
            """, conn, tx);
        cmd.Parameters.Add("@R", SqlDbType.Int).Value     = runId;
        cmd.Parameters.Add("@K", SqlDbType.Char, 3).Value = ApsRepository.KindInj;
        using var rdr = cmd.ExecuteReader();
        var list = new List<ApsInjLine>();
        while (rdr.Read())
            list.Add(new ApsInjLine((int)rdr["PlanLineID"], (string)rdr["ItemNo"], (string)rdr["LineID"],
                                    DateOnly.FromDateTime((DateTime)rdr["PlanDate"]),
                                    rdr.GetDecimal(rdr.GetOrdinal("PlanDay")), rdr.GetDecimal(rdr.GetOrdinal("PlanNight")),
                                    rdr["WoID"] as int?, (bool)rdr["SameItem"]));
        return list;
    }

    /// <summary>저장본 BundleJson 의 BOM 간선(재현 정본 — 실행을 저장한 시점의 부모↔사출 자식·QtyPer). 없거나 깨졌으면 빈 목록.</summary>
    static List<BomEdge> ReadBundleBom(SqlConnection conn, SqlTransaction tx, int runId)
    {
        using var cmd = new SqlCommand("SELECT BundleJson FROM dbo.PP_ApsRun WHERE RunID = @R;", conn, tx);
        cmd.Parameters.Add("@R", SqlDbType.Int).Value = runId;
        var json = cmd.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<PlanBundle>(json, ApsJson.Options)?.Bom ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>같은 실행의 ASM 행 공급(품번 대문자, 날짜) — BOM 규칙 자식 몫을 부모에 나눌 비율(스펙 2026-10-05 §1).</summary>
    static Dictionary<(string Item, DateOnly Date), decimal> ReadApsAsmSupply(SqlConnection conn, SqlTransaction tx, int runId)
    {
        using var cmd = new SqlCommand("SELECT ItemNo, PlanDate, Supply FROM dbo.PP_ApsPlanLine WHERE RunID = @R AND Kind = @K;", conn, tx);
        cmd.Parameters.Add("@R", SqlDbType.Int).Value     = runId;
        cmd.Parameters.Add("@K", SqlDbType.Char, 3).Value = ApsRepository.KindAsm;
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<(string, DateOnly), decimal>();
        while (rdr.Read())
            map[(((string)rdr["ItemNo"]).ToUpperInvariant(), DateOnly.FromDateTime((DateTime)rdr["PlanDate"]))] = rdr.GetDecimal(rdr.GetOrdinal("Supply"));
        return map;
    }

    /// <summary>같은 실행의 ASM 행 라인(품번 → 완제품 라인). 같은 품번 규칙 행은 ASM 행이 항상 있다.</summary>
    static Dictionary<string, string> ReadApsAsmLines(SqlConnection conn, SqlTransaction tx, int runId)
    {
        using var cmd = new SqlCommand("""
            SELECT ItemNo, MIN(LineID) AS LineID
            FROM   dbo.PP_ApsPlanLine
            WHERE  RunID = @R AND Kind = @K
            GROUP  BY ItemNo;
            """, conn, tx);
        cmd.Parameters.Add("@R", SqlDbType.Int).Value     = runId;
        cmd.Parameters.Add("@K", SqlDbType.Char, 3).Value = ApsRepository.KindAsm;
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read()) map[(string)rdr["ItemNo"]] = (string)rdr["LineID"];
        return map;
    }

    /// <summary>IN (@I0, @I1, …) 파라미터를 붙이고 자리표시 문자열을 돌려준다.</summary>
    static string AddItemParams(SqlCommand cmd, IReadOnlyList<string> items)
    {
        var names = new string[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            names[i] = $"@I{i}";
            cmd.Parameters.Add(names[i], SqlDbType.VarChar, 20).Value = items[i];
        }
        return string.Join(",", names);
    }

    static Dictionary<string, int?> ReadItemBoxQty(SqlConnection conn, SqlTransaction tx, IEnumerable<string> itemNos)
    {
        var map   = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var items = itemNos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0) return map;
        using var cmd = new SqlCommand("", conn, tx);
        cmd.CommandText = $"SELECT ItemNo, BoxQty FROM dbo.MD_Item WHERE ItemNo IN ({AddItemParams(cmd, items)});";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) map[(string)rdr["ItemNo"]] = rdr["BoxQty"] as int?;
        return map;
    }

    static Dictionary<string, string?> ReadItemRouting(SqlConnection conn, SqlTransaction tx, IEnumerable<string> itemNos)
    {
        var map   = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var items = itemNos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0) return map;
        using var cmd = new SqlCommand("", conn, tx);
        cmd.CommandText = $"SELECT ItemNo, RoutingType FROM dbo.MD_Item WHERE ItemNo IN ({AddItemParams(cmd, items)});";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) map[(string)rdr["ItemNo"]] = rdr["RoutingType"] as string;
        return map;
    }

    /// <summary>Confirmed 수주의 잔량 = OrderQty − ShippedQty − 기발행 WO 수량(취소 제외). 잔량 ≤ 0 은 배정기가 버린다.</summary>
    static List<ApsWoAllocator.OpenOrder> ReadOpenOrders(SqlConnection conn, SqlTransaction tx, IEnumerable<string> itemNos)
    {
        var list  = new List<ApsWoAllocator.OpenOrder>();
        var items = itemNos.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0) return list;
        using var cmd = new SqlCommand("", conn, tx);
        cmd.CommandText = $"""
            SELECT s.SoID, s.ItemNo, s.SoNumber, s.SoLineNo, s.RequestedDeliveryDate,
                   ISNULL(s.OrderQty,0) - ISNULL(s.ShippedQty,0) - ISNULL(iss.Qty,0) AS RemainQty
            FROM   dbo.PP_CustomerOrder s
            OUTER APPLY (SELECT SUM(ISNULL(w.OrderQty,0)) AS Qty FROM dbo.PP_WorkOrder w
                         WHERE w.SoID = s.SoID AND ISNULL(w.Status,'') <> 'Cancelled') iss
            WHERE  s.Status = 'Confirmed' AND s.ItemNo IN ({AddItemParams(cmd, items)});
            """;
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            list.Add(new ApsWoAllocator.OpenOrder((int)rdr["SoID"], (string)rdr["ItemNo"], rdr["SoNumber"] as string, rdr["SoLineNo"] as int?,
                                                  rdr["RequestedDeliveryDate"] is DateTime d ? DateOnly.FromDateTime(d) : null,
                                                  Convert.ToDecimal(rdr["RemainQty"])));
        return list;
    }

    static decimal? ReadMoldLineUph(SqlConnection conn, SqlTransaction tx, string moldId, string lineId)
    {
        using var cmd = new SqlCommand("SELECT UPH FROM dbo.MD_MoldLine WHERE MoldID = @M AND LineCode = @L;", conn, tx);
        cmd.Parameters.Add("@M", SqlDbType.VarChar, 20).Value = moldId;
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToDecimal(v);
    }

    /// <summary>
    /// 그 날·그 라인·그 품번의 기존 WO 슬롯 수량을 슬롯 StartMin 의 교대로 주/야 나눠 합산 — ApsRepository.ReadRegisteredSlots(§4.5)와
    /// 같은 필터(취소 WO 제외, 수량 &gt; 0, 0분 형제 슬롯 포함)라 계획 행에 잠긴 칸으로 더해진 양과 정확히 같은 양을 뺀다.
    /// </summary>
    static (decimal Day, decimal Night) ReadScheduledQty(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date, string itemNo,
                                                          ShiftBands.DayNightBands full)
    {
        using var cmd = new SqlCommand("""
            SELECT ISNULL(s.StartMin, 0) AS StartMin, ISNULL(s.PlannedQty, 0) AS Qty
            FROM   dbo.PP_LineSchedule s
            JOIN   dbo.PP_WorkOrder w ON w.WoID = s.WoID
            WHERE  s.LineID = @L AND s.ScheduleDate = @D AND s.EntryType = 'WO'
              AND  w.ItemNo = @I AND ISNULL(w.Status, '') <> 'Cancelled'
              AND  ISNULL(s.PlannedQty, 0) > 0;
            """, conn, tx);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@D", SqlDbType.Date).Value        = date.Date;
        cmd.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = itemNo;
        using var rdr = cmd.ExecuteReader();
        decimal day = 0, night = 0;
        while (rdr.Read())
        {
            var q = Convert.ToDecimal(rdr["Qty"]);
            if (ShiftBands.IsDay(full, Convert.ToInt32(rdr["StartMin"]))) day += q; else night += q;
        }
        return (day, night);
    }

    static SqlCommand BuildInsertApsWo(SqlConnection conn, SqlTransaction tx, string actor)
    {
        var ins = new SqlCommand(InsertApsWoSql, conn, tx);
        ins.Parameters.Add("@Wo",       SqlDbType.VarChar, 20);
        ins.Parameters.Add("@SoID",     SqlDbType.Int);
        ins.Parameters.Add("@ItemNo",   SqlDbType.VarChar, 20);
        var qty = ins.Parameters.Add("@Qty", SqlDbType.Decimal); qty.Precision = 14; qty.Scale = 3;
        ins.Parameters.Add("@Due",      SqlDbType.Date);
        ins.Parameters.Add("@Deadline", SqlDbType.Date);
        ins.Parameters.Add("@Actor",    SqlDbType.NVarChar, 20).Value = actor;
        return ins;
    }

    static int ExecInsertApsWo(SqlCommand ins, string wo, int? soId, string itemNo, decimal qty, DateTime due, DateTime deadline)
    {
        ins.Parameters["@Wo"].Value       = wo;
        ins.Parameters["@SoID"].Value     = (object?)soId ?? DBNull.Value;
        ins.Parameters["@ItemNo"].Value   = itemNo;
        ins.Parameters["@Qty"].Value      = qty;
        ins.Parameters["@Due"].Value      = due.Date;
        ins.Parameters["@Deadline"].Value = deadline.Date;
        return ins.ExecuteScalar() is int woId ? woId
             : throw new InvalidOperationException($"{wo}: {itemNo} has no RoutingType — WO not inserted.");
    }

    static void InsertApsRunWo(SqlConnection conn, SqlTransaction tx, int runId, int planLineId, int woId, int? soId, decimal qty, string actor)
    {
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.PP_ApsRunWo (RunID, PlanLineID, WoID, SoID, Qty, CreatedBy, CreatedTS)
            VALUES (@R, @P, @W, @S, @Q, @By, SYSDATETIME());
            """, conn, tx);
        cmd.Parameters.Add("@R",  SqlDbType.Int).Value = runId;
        cmd.Parameters.Add("@P",  SqlDbType.Int).Value = planLineId;
        cmd.Parameters.Add("@W",  SqlDbType.Int).Value = woId;
        cmd.Parameters.Add("@S",  SqlDbType.Int).Value = (object?)soId ?? DBNull.Value;
        var q = cmd.Parameters.Add("@Q", SqlDbType.Decimal); q.Precision = 14; q.Scale = 3; q.Value = qty;
        cmd.Parameters.Add("@By", SqlDbType.NVarChar, 20).Value = actor;
        cmd.ExecuteNonQuery();
    }

    static void UpdatePlanLineWo(SqlConnection conn, SqlTransaction tx, int planLineId, int woId)
    {
        using var cmd = new SqlCommand("UPDATE dbo.PP_ApsPlanLine SET WoID = @W WHERE PlanLineID = @P AND WoID IS NULL;", conn, tx);
        cmd.Parameters.Add("@W", SqlDbType.Int).Value = woId;
        cmd.Parameters.Add("@P", SqlDbType.Int).Value = planLineId;
        cmd.ExecuteNonQuery();
    }

    static void MarkRunReleased(SqlConnection conn, SqlTransaction tx, int runId, string actor)
    {
        using var cmd = new SqlCommand("""
            UPDATE dbo.PP_ApsRun SET Status = @S, ModifiedBy = @By, ModifiedTS = SYSDATETIME() WHERE RunID = @R;
            """, conn, tx);
        cmd.Parameters.Add("@S",  SqlDbType.VarChar, 20).Value  = ApsRepository.StatusReleased;
        cmd.Parameters.Add("@By", SqlDbType.NVarChar, 20).Value = actor;
        cmd.Parameters.Add("@R",  SqlDbType.Int).Value          = runId;
        cmd.ExecuteNonQuery();
    }
}
