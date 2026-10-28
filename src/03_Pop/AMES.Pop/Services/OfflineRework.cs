using System.Text.Json;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

/// <summary>
/// 재작업 스테이션 오프라인(10-28 사용자 결정). 대기열·오늘 판정·칩·원인 코드는 마지막으로 읽은 사본(<see cref="OfflineReads"/>)이고,
/// 오프라인에서 한 수리·폐기는 공용 큐에 올라가 복구 때 판정 시각을 넘겨 같은 저장소 메서드(<c>ReworkRepository.Rework·Scrap</c>)로 반영된다.
/// 큐에 남은 판정은 온라인 조회 결과에서도 빼고 보인다(서버에 아직 없는 판정을 다시 대기로 보이지 않게).
/// 한계: 다른 터미널이 오프라인 동안 등록한 불량은 사본에 없어 여기서 판정할 수 없다.
/// </summary>
internal static class OfflineRework
{
    public const string Process = "RWK";
    public const string KindRework = "Rework", KindScrap = "Scrap";

    private const string KeyPending = "rwk.pending", KeyDecided = "rwk.decided", KeyStats = "rwk.stats";
    private static string KeyCauses(string process) => "rwk.causes." + process.ToUpperInvariant();

    public static void Warm()
    {
        OfflineReads.Put(KeyPending, PopServices.Rework.ListPending());
        OfflineReads.Put(KeyDecided, PopServices.Rework.ListTodayDecided());
        OfflineReads.Put(KeyStats, PopServices.Rework.GetTodayStats());
        foreach (var p in new[] { "INJ", "IMG", "PNT" })
            OfflineReads.Put(KeyCauses(p), PopServices.Defects.ListCausesForProcess(p));
    }

    /// <summary>대기열 사본을 받은 시각 — 오프라인 안내에 보인다.</summary>
    public static DateTime? TakenAt => OfflineReads.TakenAt(KeyPending);

    private static List<WorkOp> Queued() => OfflineWork.PendingOps(o => o.Process == Process);

    public static List<ReworkItemDto> ListPending()
    {
        var queued = Queued().Select(o => o.Ref).ToHashSet();
        return OfflineReads.Get(KeyPending, () => PopServices.Rework.ListPending(), new List<ReworkItemDto>())
                           .Where(r => !queued.Contains(r.DefectId)).ToList();
    }

    /// <summary>오늘 판정 — 큐에 남은 오프라인 판정을 맨 위에 얹는다.</summary>
    public static List<ReworkItemDto> ListTodayDecided()
    {
        var local = Queued().Select(Decided).Where(r => r is not null).Select(r => r!).Reverse().ToList();
        var ids = local.Select(r => r.DefectId).ToHashSet();
        var server = OfflineReads.Get(KeyDecided, () => PopServices.Rework.ListTodayDecided(), new List<ReworkItemDto>());
        return local.Concat(server.Where(r => !ids.Contains(r.DefectId))).ToList();
    }

    public static ReworkStatsDto GetTodayStats()
    {
        var s = OfflineReads.Get(KeyStats, () => PopServices.Rework.GetTodayStats(),
                                 new ReworkStatsDto { Pending = 0, TodayReworked = 0, TodayScrapped = 0 });
        var q = Queued();
        if (q.Count == 0) return s;
        return new ReworkStatsDto
        {
            Pending       = Math.Max(0, s.Pending - q.Count),
            TodayReworked = s.TodayReworked + q.Count(o => o.Kind == KindRework),
            TodayScrapped = s.TodayScrapped + q.Count(o => o.Kind == KindScrap),
        };
    }

    public static ReworkItemDto? FindPendingByLotCode(string lotCode)
    {
        if (!PopDb.IsOffline)
        {
            try
            {
                var row = PopServices.Rework.FindPendingByLotCode(lotCode);
                return row is not null && Queued().Any(o => o.Ref == row.DefectId) ? null : row;
            }
            catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { /* 사본으로 */ }
        }
        return ListPending().FirstOrDefault(r => string.Equals(r.LotCode, lotCode, StringComparison.OrdinalIgnoreCase));
    }

    public static List<DefectCauseDto> Causes(string processCode)
        => OfflineReads.Get(KeyCauses(processCode), () => PopServices.Defects.ListCausesForProcess(processCode), new List<DefectCauseDto>());

    /// <summary>수리 완료 → 양품. 온라인이면 바로, 끊겼으면(또는 연결 오류면) 큐에 올리고 Done.</summary>
    public static ReworkOutcome Rework(ReworkItemDto row, string cause, string? note, PopSessionDto s, out BackflushResult backflush)
    {
        backflush = BackflushResult.None;
        if (!PopDb.IsOffline)
        {
            try { return PopServices.Rework.Rework(row.DefectId, cause, note, s.LineId, s.OperatorId, s.SessionId, s.EmployeeNo, out backflush); }
            catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { /* 큐로 */ }
        }
        return Queue(KindRework, row, cause, note, s);
    }

    public static ReworkOutcome Scrap(ReworkItemDto row, string cause, string? note, PopSessionDto s,
                                      out PalletStockRepository.PalletUnloadResult unload, out BackflushResult backflush)
    {
        unload = PalletStockRepository.PalletUnloadResult.None;
        backflush = BackflushResult.None;
        if (!PopDb.IsOffline)
        {
            try { return PopServices.Rework.Scrap(row.DefectId, cause, note, s.OperatorId, s.EmployeeNo, out unload, out backflush); }
            catch (Exception ex) when (DbHealth.IsConnectionFailure(ex)) { /* 큐로 */ }
        }
        return Queue(KindScrap, row, cause, note, s);
    }

    private static ReworkOutcome Queue(string kind, ReworkItemDto row, string cause, string? note, PopSessionDto s)
    {
        if (Queued().Any(o => o.Ref == row.DefectId)) return ReworkOutcome.AlreadyDecided;
        var op = new WorkOp
        {
            Process = Process, Kind = kind, At = DbClock.Now, LineId = s.LineId, StationCode = s.TerminalId,
            OperatorId = s.OperatorId, EmployeeNo = s.EmployeeNo, SessionId = s.SessionId,
            LotCode = row.LotCode, ItemNo = row.ItemNo, Ref = row.DefectId,
        };
        op.Args["Cause"] = cause;
        op.Args["Note"]  = note;
        op.Args["Row"]   = JsonSerializer.Serialize(row);
        OfflineWork.Enqueue(op);
        return ReworkOutcome.Done;
    }

    // 큐의 판정 한 건을 이력 행으로 — 화면의 「오늘 판정」 탭에 바로 보인다.
    private static ReworkItemDto? Decided(WorkOp op)
    {
        ReworkItemDto? r;
        try { r = JsonSerializer.Deserialize<ReworkItemDto>(op.Arg("Row") ?? ""); }
        catch (JsonException) { return null; }
        if (r is null) return null;
        return new ReworkItemDto
        {
            DefectId = r.DefectId, LotId = r.LotId, LotCode = r.LotCode, ItemNo = r.ItemNo, ItemName = r.ItemName,
            ProcessCode = r.ProcessCode, LineId = r.LineId, DefectCode = r.DefectCode, DefectName = r.DefectName, DefectNameEn = r.DefectNameEn,
            DefaultCauseCode = r.DefaultCauseCode, PriorStatus = r.PriorStatus, ReasonNote = r.ReasonNote, DetectedAt = r.DetectedAt,
            RegisteredBy = r.RegisteredBy, WoId = r.WoId, WoNumber = r.WoNumber,
            Disposition = op.Kind == KindRework ? LotDefectRules.DispositionReworked : LotDefectRules.DispositionScrapped,
            CauseCode = op.Arg("Cause"), CorrectiveAction = op.Arg("Note"), DispositionBy = op.EmployeeNo, DispositionAt = op.At,
        };
    }

    /// <summary>복구 때 판정 한 건 반영 — 다른 터미널이 먼저 판정했으면 충돌.</summary>
    public static string? Apply(WorkOp op, int? session)
    {
        var cause = op.Arg("Cause") ?? "";
        var outcome = op.Kind == KindRework
            ? PopServices.Rework.Rework(op.Ref, cause, op.Arg("Note"), op.LineId, op.OperatorId, session, op.EmployeeNo, out _, op.At)
            : PopServices.Rework.Scrap(op.Ref, cause, op.Arg("Note"), op.OperatorId, op.EmployeeNo, out _, out _, op.At);
        return outcome == ReworkOutcome.Done ? null : outcome.ToString();
    }
}
