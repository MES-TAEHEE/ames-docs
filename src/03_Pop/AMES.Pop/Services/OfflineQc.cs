using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

/// <summary>
/// QC 화면 오프라인(10-28 사용자 결정). 조회는 마지막으로 읽은 사본(<see cref="OfflineReads"/>), 쓰기 중 검사 시작·판정(QC-01~03)·NCR 등록(QC-04)·
/// 홀드 해제(QC-05)는 공용 큐에 올라가 복구 때 발생 시각을 넘겨 같은 저장소 메서드로 반영된다. 오프라인에서 시작한 검사·NCR 은 임시(음수) 번호로
/// 보이고 반영 때 실제 번호가 된다. 오프라인 검사 대상은 확인하지 못하므로 스캔 값을 그대로 받고 복구 때 서버가 LOT·품번으로 해석한다(없으면 충돌).
/// 추적(QC-TRC)은 이 PC 에서 온라인으로 본 LOT 만 사본으로 보인다.
/// </summary>
internal static class OfflineQc
{
    public const string Process = "QC";
    public const string KindCreate = "InspCreate", KindFinalize = "InspFinalize", KindNcr = "NcrCreate", KindRelease = "HoldRelease";

    private static string KeyInsp(string type, int n) => $"qc.insp.{type}.{n}";
    private const string NcrOpen = "Open,Investigating", HoldAll = "Held,Released,Rejected", CapaOpen = "Open,In Progress,Plan,Action,Verify";

    public static void Warm()
    {
        foreach (var (t, n) in new[] { ("Incoming", 30), ("InProcess", 30), ("Final", 30), ("", 100) })
            OfflineReads.Put(KeyInsp(t, n), PopServices.Qc.ListInspections(t, n));
        OfflineReads.Put($"qc.ncr.{NcrOpen}.60", PopServices.Qc.ListNcrs(NcrOpen, 60));
        OfflineReads.Put("qc.ncr.Closed.60", PopServices.Qc.ListNcrs("Closed", 60));
        OfflineReads.Put($"qc.hold.{HoldAll}.40", PopServices.Qc.ListHolds(HoldAll, 40));
        OfflineReads.Put($"qc.capa.{CapaOpen}.30", PopServices.Qc.ListCapas(CapaOpen, 30));
        OfflineReads.Put("qc.modules", PopServices.Qc.GetModuleDefectRates());
        OfflineReads.Put("qc.pareto.6", PopServices.Qc.GetDefectPareto(6));
        var (n1, n2, n3) = PopServices.Qc.GetDashboardCounts();
        OfflineReads.Put("qc.counts", new[] { n1, n2, n3 });
        OfflineReads.Put("qc.stds", PopServices.Qc.ListInspectionStds());
    }

    private static List<WorkOp> Queued(string kind) => OfflineWork.PendingOps(o => o.Process == Process && o.Kind == kind);

    private static WorkOp NewOp(string kind, int reference, PopSessionDto? s, string label) => new()
    {
        Process = Process, Kind = kind, At = DbClock.Now, Ref = reference, LineId = s?.LineId ?? "", StationCode = s?.TerminalId ?? "",
        OperatorId = s?.OperatorId ?? "system", EmployeeNo = s?.EmployeeNo ?? "Q000", SessionId = s?.SessionId ?? 0, LotCode = label,
    };

    private static bool Offline => PopDb.IsOffline;

    // 온라인 쓰기 — 연결 오류면 false 를 돌려 호출자가 큐로 보낸다.
    private static bool TryOnline(Action act)
    {
        if (Offline) return false;
        try { act(); return true; }
        catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { return false; }
    }

    // ── QC-01·02·03 검사 ────────────────────────────────────────────────

    /// <summary>검사 목록 — 사본 + 오프라인에서 시작한 검사, 오프라인 판정을 얹는다. type 이 빈 값이면 전체.</summary>
    public static List<QcInspectionDto> ListInspections(string type, int topN)
    {
        var server = OfflineReads.Get(KeyInsp(type, topN), () => PopServices.Qc.ListInspections(type, topN), new List<QcInspectionDto>());
        var finals = Queued(KindFinalize).GroupBy(o => o.Ref).ToDictionary(g => g.Key, g => g.Last());
        var local = Queued(KindCreate)
            .Where(o => type.Length == 0 || o.Arg("Type") == type)
            .Select(o => new QcInspectionDto
            {
                InspectionId = o.Ref, InspectionNo = $"OFFLINE {-o.Ref}", InspectionType = o.Arg("Type") ?? "", LineId = o.LineId,
                ItemNo = o.ItemNo, ItemName = o.ItemNo, SampleSize = int.TryParse(o.Arg("Sample"), out var n) ? n : 0,
                Verdict = "IN_PROGRESS", InspectorId = o.OperatorId, InsStartTs = o.At,
            }).Reverse();
        return local.Concat(server).Select(i => finals.TryGetValue(i.InspectionId, out var f) && i.Verdict == "IN_PROGRESS"
            ? Copy(i, f.Arg("Verdict") ?? i.Verdict, f.At) : i).ToList();
    }

    private static QcInspectionDto Copy(QcInspectionDto i, string verdict, DateTime end) => new()
    {
        InspectionId = i.InspectionId, InspectionNo = i.InspectionNo, InspectionType = i.InspectionType, LotId = i.LotId, WoId = i.WoId,
        LineId = i.LineId, ItemNo = i.ItemNo, ItemName = i.ItemName, Mode = i.Mode, SampleSize = i.SampleSize, BatchQty = i.BatchQty,
        CumulativeGood = i.CumulativeGood, DefectQtyTotal = i.DefectQtyTotal, Verdict = verdict, CriticalFlag = i.CriticalFlag,
        InspectorId = i.InspectorId, InsStartTs = i.InsStartTs, InsEndTs = end,
    };

    public static List<QcInspectionItemDto> ListInspectionItems(int inspectionId)
        => inspectionId < 0 ? new()
           : OfflineReads.Get($"qc.items.{inspectionId}", () => PopServices.Qc.ListInspectionItems(inspectionId), new List<QcInspectionItemDto>());

    public sealed record StartResult(int Id, string ItemNo, bool Queued);

    /// <summary>
    /// 스캔한 LOT·품번으로 검사를 시작한다. 온라인이면 해석해서 바로 만들고(대상 없음 → null), 끊겼으면 스캔 값 그대로 큐에 올린다 —
    /// 대상 확인은 복구 때 서버가 한다.
    /// </summary>
    public static StartResult? StartInspection(string type, string code, int sampleSize, PopSessionDto? s)
    {
        QcRepository.InspectionTarget? target = null;
        int id = 0;
        var notFound = false;
        if (TryOnline(() =>
            {
                target = PopServices.Qc.ResolveInspectionTarget(code);
                if (target is null) { notFound = true; return; }
                id = PopServices.Qc.CreateInspection(type, target, s?.LineId, sampleSize, s?.OperatorId ?? "system", s?.EmployeeNo ?? "Q000");
            }))
            return notFound ? null : new StartResult(id, target!.ItemNo, false);

        var op = NewOp(KindCreate, OfflineWork.NextTempId(), s, $"QC {type} {code}");
        op.ItemNo = code;
        op.Args["Type"] = type; op.Args["Code"] = code; op.Args["Sample"] = sampleSize.ToString();
        OfflineWork.Enqueue(op);
        return new StartResult(op.Ref, code, true);
    }

    /// <summary>판정. 이미 판정된 검사면 false. 끊겼으면 큐 — 오프라인에서 시작한 검사(음수 ID)도 판정할 수 있다.</summary>
    public static bool FinalizeInspection(int inspectionId, string verdict, PopSessionDto? s)
    {
        var done = false;
        if (inspectionId > 0 && TryOnline(() => done = PopServices.Qc.FinalizeInspection(inspectionId, verdict, s?.OperatorId ?? "system", s?.EmployeeNo ?? "Q000")))
            return done;

        var cur = ListInspections("", 100).Concat(Queued(KindCreate).Select(o => new QcInspectionDto { InspectionId = o.Ref, Verdict = "IN_PROGRESS" }))
                                          .FirstOrDefault(i => i.InspectionId == inspectionId);
        if (Queued(KindFinalize).Any(o => o.Ref == inspectionId) || (cur is not null && cur.Verdict != "IN_PROGRESS")) return false;
        var op = NewOp(KindFinalize, inspectionId, s, $"QC #{inspectionId}");
        op.Args["Verdict"] = verdict;
        OfflineWork.Enqueue(op);
        return true;
    }

    // ── QC-04 NCR ───────────────────────────────────────────────────────

    public static List<QcNcrDto> ListNcrs(string statusFilter, int topN)
    {
        var server = OfflineReads.Get($"qc.ncr.{statusFilter}.{topN}", () => PopServices.Qc.ListNcrs(statusFilter, topN), new List<QcNcrDto>());
        if (!statusFilter.Split(',').Contains("Open")) return server;
        var local = Queued(KindNcr).Select(o => new QcNcrDto
        {
            NcrId = o.Ref, NcrNumber = $"OFFLINE {-o.Ref}", SourceType = o.Arg("Src"), Severity = o.Arg("Sev"), ItemNo = o.ItemNo,
            ItemName = o.ItemNo, AffectedQty = decimal.TryParse(o.Arg("Qty"), out var q) ? q : 0, Disposition = o.Arg("Disp"),
            Status = "Open", ReportedBy = o.EmployeeNo, ReportedAt = o.At,
        }).Reverse();
        return local.Concat(server).ToList();
    }

    /// <returns>NCR 번호(ID). 끊겼으면 임시(음수) ID.</returns>
    public static int CreateNcr(string severity, string sourceType, string itemNo, decimal qty, string disposition, PopSessionDto? s)
    {
        var id = 0;
        if (TryOnline(() => id = PopServices.Qc.CreateNcr(severity, sourceType, null, itemNo, qty, disposition,
                                                         s?.OperatorId ?? "system", s?.EmployeeNo ?? "Q000")))
            return id;
        var op = NewOp(KindNcr, OfflineWork.NextTempId(), s, $"NCR {itemNo}");
        op.ItemNo = itemNo;
        op.Args["Sev"] = severity; op.Args["Src"] = sourceType; op.Args["Qty"] = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        op.Args["Disp"] = disposition;
        OfflineWork.Enqueue(op);
        return op.Ref;
    }

    // ── QC-05 홀드 ──────────────────────────────────────────────────────

    public static List<QcHoldDto> ListHolds(string statusFilter, int topN)
    {
        var server = OfflineReads.Get($"qc.hold.{statusFilter}.{topN}", () => PopServices.Qc.ListHolds(statusFilter, topN), new List<QcHoldDto>());
        var released = Queued(KindRelease).GroupBy(o => o.Ref).ToDictionary(g => g.Key, g => g.Last().Arg("Action"));
        if (released.Count == 0) return server;
        return server.Select(h => h.Status == "Held" && released.TryGetValue(h.HoldId, out var act) ? new QcHoldDto
        {
            HoldId = h.HoldId, HoldNumber = h.HoldNumber, SourceNcrId = h.SourceNcrId, NcrNumber = h.NcrNumber, Severity = h.Severity,
            AffectedType = h.AffectedType, LotId = h.LotId, ItemNo = h.ItemNo, ItemName = h.ItemName, HeldQty = h.HeldQty,
            PhysicalLocation = h.PhysicalLocation, HeldAt = h.HeldAt,
            Status = act is "USE-AS-IS" or "REWORK" ? "Released" : "Rejected",   // SP_POP_QC_ReleaseHold_UpdHold 와 같은 규칙
        } : h).ToList();
    }

    public static bool ReleaseHold(int holdId, string action, string reason, PopSessionDto? s)
    {
        var done = false;
        if (TryOnline(() => done = PopServices.Qc.ReleaseHold(holdId, action, reason, s?.OperatorId ?? "system", s?.EmployeeNo ?? "Q000")))
            return done;
        var cur = ListHolds(HoldAll, 40).FirstOrDefault(h => h.HoldId == holdId);
        if (cur is not null && cur.Status != "Held") return false;
        var op = NewOp(KindRelease, holdId, s, $"HOLD #{holdId}");
        op.Args["Action"] = action; op.Args["Reason"] = reason;
        OfflineWork.Enqueue(op);
        return true;
    }

    // ── 조회 전용 (QC-06·07·08·TRC) ─────────────────────────────────────

    public static List<QcCapaDto> ListCapas(string statusFilter, int topN)
        => OfflineReads.Get($"qc.capa.{statusFilter}.{topN}", () => PopServices.Qc.ListCapas(statusFilter, topN), new List<QcCapaDto>());

    public static List<QcCapaActionDto> ListCapaActions(int capaId)
        => OfflineReads.Get($"qc.capaact.{capaId}", () => PopServices.Qc.ListCapaActions(capaId), new List<QcCapaActionDto>());

    public static List<QcModuleDefectDto> GetModuleDefectRates()
        => OfflineReads.Get("qc.modules", () => PopServices.Qc.GetModuleDefectRates(), new List<QcModuleDefectDto>());

    public static List<QcDefectParetoDto> GetDefectPareto(int topN)
        => OfflineReads.Get($"qc.pareto.{topN}", () => PopServices.Qc.GetDefectPareto(topN), new List<QcDefectParetoDto>());

    public static (int OpenNcr, int ActiveHold, int OpenCapa) GetDashboardCounts()
    {
        var c = OfflineReads.Get("qc.counts", () => { var (a, b, d) = PopServices.Qc.GetDashboardCounts(); return new[] { a, b, d }; }, new[] { 0, 0, 0 });
        return c.Length == 3 ? (c[0], c[1], c[2]) : (0, 0, 0);
    }

    public static List<QcInspectionStdDto> ListInspectionStds()
        => OfflineReads.Get("qc.stds", () => PopServices.Qc.ListInspectionStds(), new List<QcInspectionStdDto>());

    public static int? FindLotIdByCode(string lotCode)
        => OfflineReads.Get($"qc.lotid.{lotCode.ToUpperInvariant()}", () => PopServices.Qc.FindLotIdByCode(lotCode), (int?)null);

    public static List<QcTraceNodeDto> TraceLot(int lotId)
        => OfflineReads.Get($"qc.trace.{lotId}", () => PopServices.Qc.TraceLot(lotId), new List<QcTraceNodeDto>());

    // ── 복구 때 반영 ────────────────────────────────────────────────────

    public static string? Apply(WorkOp op)
    {
        switch (op.Kind)
        {
            case KindCreate:
            {
                if (OfflineWork.ResolveId(op.Ref) is not null) return null;   // 이미 반영됐다(재시도)
                var target = PopServices.Qc.ResolveInspectionTarget(op.Arg("Code") ?? op.ItemNo);
                if (target is null) return "TargetNotFound";
                var id = PopServices.Qc.CreateInspection(op.Arg("Type") ?? "", target, op.LineId.Length == 0 ? null : op.LineId,
                    int.TryParse(op.Arg("Sample"), out var n) ? n : 0, op.OperatorId, op.EmployeeNo, op.At);
                OfflineWork.MapTempId(op.Ref, id);
                return null;
            }
            case KindFinalize:
            {
                if (OfflineWork.ResolveId(op.Ref) is not int id) return "InspectionMissing";
                return PopServices.Qc.FinalizeInspection(id, op.Arg("Verdict") ?? "", op.OperatorId, op.EmployeeNo, op.At) ? null : "AlreadyFinalized";
            }
            case KindNcr:
            {
                if (OfflineWork.ResolveId(op.Ref) is not null) return null;
                var qty = decimal.TryParse(op.Arg("Qty"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var q) ? q : 0;
                var id = PopServices.Qc.CreateNcr(op.Arg("Sev") ?? "", op.Arg("Src") ?? "", null, op.ItemNo, qty, op.Arg("Disp") ?? "",
                                                   op.OperatorId, op.EmployeeNo, op.At);
                OfflineWork.MapTempId(op.Ref, id);
                return null;
            }
            case KindRelease:
                return PopServices.Qc.ReleaseHold(op.Ref, op.Arg("Action") ?? "", op.Arg("Reason") ?? "", op.OperatorId, op.EmployeeNo, op.At)
                    ? null : "HoldNotHeld";
        }
        return $"unknown qc op {op.Kind}";
    }
}
