using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

/// <summary>사본의 LOT 한 건 — INJ 원천 LOT 과 IMG·PNT 완제품 LOT 을 한 모양으로 담는다(공정별 필드는 해당 공정만 채운다).</summary>
internal sealed class OfflineLot
{
    public int LotId { get; set; }
    public string LotCode { get; set; } = "";
    public string ItemNo { get; set; } = "";
    public string? ItemName { get; set; }
    public string? LineId { get; set; }
    public string? EquipId { get; set; }
    public string ConfirmStatus { get; set; } = LotDefectRules.Raw;
    public int PrintedCount { get; set; }
    public DateTime CreatedTS { get; set; }

    // INJ
    public string? MoldCode { get; set; }
    public string? ColorCode { get; set; }
    public string? MoldId { get; set; }
    public int? CavityNo { get; set; }
    public string? CavityPos { get; set; }
    public string? PressType { get; set; }
    public long? MachineShotCount { get; set; }
    public bool? OverallNg { get; set; }
    public string? InspectionSummary { get; set; }

    // IMG·PNT
    public string? Pgn { get; set; }
    public string? Alc { get; set; }
    public string? MountPos { get; set; }
    public string? CustomerCode { get; set; }
    public string? CoreLotCode { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public static OfflineLot From(InjLotDto d) => new()
    {
        LotId = d.LotId, LotCode = d.LotCode, ItemNo = d.ItemNo, ItemName = d.ItemName, LineId = d.LineId, EquipId = d.EquipId,
        ConfirmStatus = d.ConfirmStatus, PrintedCount = d.PrintedCount, CreatedTS = d.CreatedTS,
        MoldCode = d.MoldCode, ColorCode = d.ColorCode, MoldId = d.MoldId, CavityNo = d.CavityNo, CavityPos = d.CavityPos,
        PressType = d.PressType, MachineShotCount = d.MachineShotCount, OverallNg = d.OverallNg, InspectionSummary = d.InspectionSummary,
    };

    public static OfflineLot From(ImgLotDto d) => new()
    {
        LotId = d.LotId, LotCode = d.LotCode, ItemNo = d.ItemNo, ItemName = d.ItemName, LineId = d.LineId, EquipId = d.EquipId,
        ConfirmStatus = d.ConfirmStatus, PrintedCount = d.PrintedCount, CreatedTS = d.CreatedTS,
        Pgn = d.Pgn, Alc = d.Alc, MountPos = d.MountPos, CustomerCode = d.CustomerCode, CoreLotCode = d.CoreLotCode, ConfirmedAt = d.ConfirmedAt,
    };

    public InjLotDto ToInj() => new()
    {
        LotId = LotId, LotCode = LotCode, ItemNo = ItemNo, ItemName = ItemName, LineId = LineId, EquipId = EquipId,
        MoldCode = MoldCode, ColorCode = ColorCode, MoldId = MoldId, CavityNo = CavityNo, CavityPos = CavityPos, PressType = PressType,
        ConfirmStatus = ConfirmStatus, MachineShotCount = MachineShotCount, PrintedCount = PrintedCount, CreatedTS = CreatedTS,
        OverallNg = OverallNg, InspectionSummary = InspectionSummary,
    };

    public ImgLotDto ToImg() => new()
    {
        LotId = LotId, LotCode = LotCode, ItemNo = ItemNo, ItemName = ItemName, Pgn = Pgn, Alc = Alc, MountPos = MountPos,
        CustomerCode = CustomerCode, LineId = LineId, EquipId = EquipId, ConfirmStatus = ConfirmStatus, ConfirmedAt = ConfirmedAt,
        PrintedCount = PrintedCount, CreatedTS = CreatedTS, CoreLotCode = CoreLotCode,
    };
}

internal sealed class OfflineManualMold
{
    public string? ItemName { get; set; }
    public string? MoldCode { get; set; }
    public string? ColorCode { get; set; }
    public int? CavityNo { get; set; }
    public string? CavityPos { get; set; }
    public string? EquipId { get; set; }
}

/// <summary>완제품(IMG·PNT) 라벨에 찍는 값 — 품번 정보·수주처 코드·설비. 품번마다 하나.</summary>
internal sealed class OfflineLabel
{
    public bool Exists { get; set; }
    public string? ItemName { get; set; }
    public string? Pgn { get; set; }
    public string? Alc { get; set; }
    public string? MountPos { get; set; }
    public string? CustomerCode { get; set; }
    public string? EquipId { get; set; }
}

internal sealed class OfflineCore
{
    public string LotCode { get; set; } = "";
    public string ItemNo { get; set; } = "";
}

internal sealed class OfflineCoreMap
{
    public int WoId { get; set; }
    public string FinishedItem { get; set; } = "";
    public string CoreItem { get; set; } = "";
    public string? CustomerCode { get; set; }
}

internal sealed class OfflineBomCore
{
    public string Outcome { get; set; } = "";
    public string? CoreItemNo { get; set; }
}

/// <summary>로그인 중 주기적으로(그리고 로그아웃 때) 받아 두는 작업 사본 — 한 라인·스테이션 분.</summary>
internal sealed class OfflineWorkState
{
    public string Process { get; set; } = "INJ";
    public DateTime TakenAtUtc { get; set; }
    public string LineId { get; set; } = "";
    public string StationCode { get; set; } = "";
    public int PastDays { get; set; }
    public int FutureDays { get; set; }
    /// <summary>받을 때의 전기일 경계(분) — DB 없이 재시작해도 같은 하루 기준을 쓰게 남긴다. null = 그때 못 읽음.</summary>
    public int? DayCutoffMin { get; set; }
    /// <summary>LOT 명부 — 최근 며칠 + 확정 대기 전부. 오프라인 스캔이 이걸로 LOT 을 알아본다.</summary>
    public List<OfflineLot> Lots { get; set; } = new();
    /// <summary>날짜(yyyy-MM-dd)별 품번 현황 — 과거·미래 보관 범위 전체. 로컬 작업의 효과는 읽을 때 얹는다.</summary>
    public Dictionary<string, List<InjItemDailyDto>> Summaries { get; set; } = new();
    public List<WorkOrderDto> OpenWos { get; set; } = new();
    public Dictionary<string, MoldDto> Molds { get; set; } = new();
    public List<DefectCodeDto> DefectCodes { get; set; } = new();
    public List<PalletStockRepository.OfflinePalletRow> Pallets { get; set; } = new();
    public Dictionary<string, PalletStockRepository.OfflineItemRule> ItemRules { get; set; } = new();
    /// <summary>INJ 수동 라벨용 — 키 "금형|품번".</summary>
    public Dictionary<string, OfflineManualMold> Manual { get; set; } = new();
    /// <summary>IMG·PNT 라벨용 — 키 품번.</summary>
    public Dictionary<string, OfflineLabel> Labels { get; set; } = new();
    /// <summary>IMG — 아직 완제품에 안 쓰인 확정 사출 LOT(Core 후보).</summary>
    public List<OfflineCore> Cores { get; set; } = new();
    /// <summary>IMG — 오프라인에서 이미 쓴 Core.</summary>
    public List<string> UsedCores { get; set; } = new();
    public List<OfflineCoreMap> CoreMap { get; set; } = new();
    public Dictionary<string, OfflineBomCore> BomCores { get; set; } = new();
    /// <summary>라인 LotNo 접두어(MD_Line.LotPrefix 2자) — 오프라인 번호 헤더를 만든다.</summary>
    public string LotPrefix { get; set; } = "";
    /// <summary>온라인일 때 DB 에서 미리 받아 둔 LOT 번호 구간(헤더 하루분). 오프라인 출력은 여기서만 받는다.
    /// 구간은 SYS_LotSeq 에서 이미 빠져 있어 InjAgent 나 DB 채번과 겹치지 않는다(결번이 생길 수 있다).</summary>
    public List<OfflineLotBlock> LotBlocks { get; set; } = new();
    /// <summary>사본에 없어서 검증 없이 받아들인 팔레트 — 복구 뒤 서버가 판정한다.</summary>
    public List<string> UnverifiedPallets { get; set; } = new();
    /// <summary>오프라인에서 이 팔레트에 실은 LOT → 팔레트 (불량 등록 때 다시 내리려고).</summary>
    public Dictionary<string, string> LotPallet { get; set; } = new();
}

/// <summary>오프라인에서 한 작업 한 건. 복구되면 발생 순서대로 같은 저장소 메서드로 반영한다.</summary>
internal sealed class WorkOp
{
    public string Process { get; set; } = "INJ";
    /// <summary>Confirm | Defect | ManualLot(INJ) | CreateRaw(IMG·PNT) | CreateFromCore(IMG)</summary>
    public string Kind { get; set; } = "";
    public DateTime At { get; set; }
    public string LineId { get; set; } = "";
    public string StationCode { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string EmployeeNo { get; set; } = "";
    public int SessionId { get; set; }              // 오프라인 로그인이면 임시(음수) ID — 반영 때 실제 ID 로 바꾼다
    public string LotCode { get; set; } = "";
    public string ItemNo { get; set; } = "";
    public string? PalletNo { get; set; }
    public string? DefectCode { get; set; }
    public string? MoldId { get; set; }
    public string? CoreLotCode { get; set; }
    public string? FinishedItem { get; set; }       // CreateFromCore — 작업자가 고른 완제품(서버가 같은 규칙으로 다시 정한다)
    public string? CustomerCode { get; set; }       // 오프라인에서 라벨에 찍은 수주처 — LOT 도 이 값으로 넣는다
    public DateTime LotDate { get; set; }           // 현황 효과를 얹을 LOT 생성일
    public string PriorStatus { get; set; } = "";   // 작업 전 LOT 상태 — 사본에 없던 LOT 이면 빈 값
    public int PrintedCount { get; set; }
    /// <summary>안돈·재작업·QC(10-28) — 대상 행 ID. 오프라인에서 만든 행이면 임시(음수) ID 이고 반영 때 실제 ID 로 바꾼다.</summary>
    public int Ref { get; set; }
    /// <summary>안돈·재작업·QC 작업의 나머지 값(이름 → 값).</summary>
    public Dictionary<string, string?> Args { get; set; } = new();

    public string? Arg(string key) => Args.TryGetValue(key, out var v) ? v : null;
}

internal sealed class WorkConflict
{
    public DateTime At { get; set; }
    public string Kind { get; set; } = "";
    public string LotCode { get; set; } = "";
    public string Reason { get; set; } = "";
}

internal sealed class WorkOpsFile
{
    public List<WorkOp> Ops { get; set; } = new();
    public List<WorkConflict> Conflicts { get; set; } = new();
    /// <summary>오프라인에서 만든 안돈·부서 호출·검사 등의 임시(음수) ID — 마지막으로 준 값.</summary>
    public int LastTempId { get; set; }
    /// <summary>임시 ID → 반영 때 만든 실제 ID.</summary>
    public Dictionary<int, int> TempIds { get; set; } = new();
}

/// <summary>오프라인 MAIN 화면이 한 번 그리는 데 필요한 것 — 온라인 조회 결과와 같은 내용이다.</summary>
/// <summary>온라인에서 받아 둔 LOT 번호 구간 — Next 부터 Last 까지 아직 안 쓴 번호다(Header 5자 = 년월일+라인코드).</summary>
internal sealed record OfflineLotBlock(DateTime Day, string Header, int Next, int Last);

internal sealed record OfflineScreen(
    List<InjItemDailyDto> Items, string? SelItemNo, WorkOrderDto? ActiveWo,
    int TodayGood, int TodayDef, MoldDto? Mold,
    List<OfflineLot> RawLots, List<OfflineLot> TodayLots, int RawCount, int NgCount);

/// <summary>
/// POP 오프라인 작업(INJ·IMG·PNT). 원칙: **오프라인에서는 일단 받아들이고, 복구 뒤 서버가 판정한다** — 생산을 멈추지 않는다.
/// 사본(<see cref="OfflineWorkState"/>)에 있는 LOT·팔레트는 온라인과 같은 규칙(LotDefectRules·PalletLoadRules)으로 막고,
/// 사본에 없는 것은 받아들여 큐에 올린 뒤 복구 때 서버가 거부하면 충돌 목록에 남긴다.
/// 반영은 온라인과 같은 저장소 메서드를 쓰되 발생 시각을 넘겨 전기일·교대·불량일이 그날로 남게 한다.
/// </summary>
internal static class OfflineWork
{
    /// <summary>온라인일 때 한 번에 확보하는 오프라인 LOT 번호 수, 그 아래로 남으면 다음 갱신 때 새로 받는다.</summary>
    private const int LotBlockSize = 200;
    private const int LotBlockRefillBelow = 50;

    private const string StateFile = "work-state.bin";
    private const string OpsFile   = "work-ops.bin";
    private const int LotDetailDays = 2;
    private const int CoreDays = 1;

    private static readonly object Gate = new();
    private static OfflineWorkState? _state;
    private static bool _stateLoaded, _dirty;
    private static WorkOpsFile? _ops;
    private static int _prefetching, _replaying, _syntheticLotId;
    private static long _lastPrefetchTicks, _lastFlushTicks;

    /// <summary>복구 때 반영 결과를 화면에 알린다 — (문구, 오류 여부). 호스트가 토스트로 연결한다.</summary>
    public static event Action<string, bool>? Notice;

    // ── 설정 (설정 파일을 못 읽는 환경 — 테스트 — 에서는 기본값) ───────────

    private static T Setting<T>(Func<AppConfig, T> read, T fallback)
    {
        try { return read(AppConfig.Current); } catch { return fallback; }
    }
    private static int PastDays        => Setting(c => c.OfflinePastDays, 7);
    private static int FutureDays      => Setting(c => c.OfflineFutureDays, 7);
    private static int RefreshMinutes  => Setting(c => c.OfflineRefreshMinutes, 10);
    private static int NotFoundRetryMinutes => Setting(c => c.OfflineNotFoundRetryMinutes, 30);

    // ── 저장소 ──────────────────────────────────────────────────────────

    internal static void ResetForTests()
    {
        lock (Gate) { _state = null; _stateLoaded = false; _dirty = false; _ops = null; _syntheticLotId = 0; }
    }

    public static OfflineWorkState? State
    {
        get
        {
            lock (Gate)
            {
                if (!_stateLoaded)
                {
                    _state = OfflineStore.Load<OfflineWorkState>(StateFile); _stateLoaded = true;
                    if (!PopDay.IsLoaded && _state?.DayCutoffMin is int cut) PopDay.Set(TimeSpan.FromMinutes(cut));
                }
                return _state;
            }
        }
    }

    // Gate 를 잡고 부른다.
    private static WorkOpsFile Ops => _ops ??= OfflineStore.Load<WorkOpsFile>(OpsFile) ?? new();

    private static void SaveState(OfflineWorkState s)
    {
        try { OfflineStore.Save(StateFile, s); _dirty = false; Interlocked.Exchange(ref _lastFlushTicks, DateTime.UtcNow.Ticks); }
        catch (Exception ex) { PopLog.Append("offline", "OfflineWork", $"state save failed: {ex.Message}"); }
    }

    private static void SaveOps()
    {
        try { OfflineStore.Save(OpsFile, Ops); }
        catch (Exception ex) { PopLog.Append("offline", "OfflineWork", $"ops save failed: {ex.Message}"); }
    }

    public static int PendingCount { get { lock (Gate) return Ops.Ops.Count; } }

    public static IReadOnlyList<WorkConflict> Conflicts { get { lock (Gate) return Ops.Conflicts.ToList(); } }

    public static void ClearConflicts() { lock (Gate) { Ops.Conflicts.Clear(); SaveOps(); } }

    // ── 안돈·재작업·QC 큐 (10-28) — 같은 큐·같은 반영 순서·같은 충돌 목록을 쓴다 ──

    /// <summary>오프라인 작업 한 건을 큐 끝에 올린다.</summary>
    public static void Enqueue(WorkOp op) { lock (Gate) { Ops.Ops.Add(op); SaveOps(); } }

    /// <summary>아직 반영 못 한 작업 중 조건에 맞는 것(발생 순).</summary>
    public static List<WorkOp> PendingOps(Func<WorkOp, bool> filter) { lock (Gate) return Ops.Ops.Where(filter).ToList(); }

    /// <summary>오프라인에서 만드는 행의 임시 ID — 음수라 실제 ID 와 겹치지 않는다.</summary>
    public static int NextTempId() { lock (Gate) { var id = --Ops.LastTempId; SaveOps(); return id; } }

    /// <summary>임시 ID 면 반영 때 만든 실제 ID(아직이면 null), 실제 ID 면 그대로.</summary>
    public static int? ResolveId(int id)
    {
        if (id >= 0) return id;
        lock (Gate) return Ops.TempIds.TryGetValue(id, out var real) ? real : null;
    }

    public static void MapTempId(int tempId, int realId) { lock (Gate) { Ops.TempIds[tempId] = realId; SaveOps(); } }

    /// <summary>이 라인·스테이션 사본이 있고 유효 기간 안인가.</summary>
    public static bool HasData(string lineId, string stationCode)
        => State is { } s
           && string.Equals(s.LineId, lineId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(s.StationCode, stationCode, StringComparison.OrdinalIgnoreCase)
           && DateTime.UtcNow - s.TakenAtUtc <= OfflineAuth.MaxAge;

    // ── 사본 받기 ───────────────────────────────────────────────────────

    /// <summary>로그인 중에는 설정 주기마다, 로그아웃 때는 즉시(force) 받는다. 화면을 막지 않게 백그라운드로 돈다.</summary>
    public static void PrefetchInBackground(string lineId, string stationCode, string process, bool force = false)
    {
        if (PopDb.IsOffline) return;
        var now = DateTime.UtcNow;
        if (!force && State is { } s && string.Equals(s.LineId, lineId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.StationCode, stationCode, StringComparison.OrdinalIgnoreCase)
            && s.Process == process
            && now - s.TakenAtUtc < TimeSpan.FromMinutes(RefreshMinutes)) return;
        if (now.Ticks - Interlocked.Read(ref _lastPrefetchTicks) < TimeSpan.FromMinutes(1).Ticks) return;
        if (Interlocked.CompareExchange(ref _prefetching, 1, 0) != 0) return;
        Interlocked.Exchange(ref _lastPrefetchTicks, now.Ticks);
        _ = Task.Run(() =>
        {
            try { Prefetch(lineId, stationCode, process); }
            catch (Exception ex) { PopLog.Append("offline", "OfflineWork", $"prefetch failed: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _prefetching, 0); }
        });
    }

    /// <summary>
    /// DB 에서 이 라인·스테이션의 작업 사본을 통째로 받아 바꾼다. 아직 반영 못 한 오프라인 작업이 있으면 하지 않는다 —
    /// 서버 값으로 덮으면 로컬 효과(확정·불량·새 LOT)가 사라진다.
    /// </summary>
    public static void Prefetch(string lineId, string stationCode, string process)
    {
        if (PopDb.IsOffline || PendingCount > 0) return;

        var today = DbClock.Today;
        var past = PastDays;
        var future = FutureDays;
        var since = today.AddDays(-Math.Min(past, LotDetailDays));

        var next = new OfflineWorkState
        {
            Process = process, TakenAtUtc = DateTime.UtcNow, LineId = lineId, StationCode = stationCode, PastDays = past, FutureDays = future,
            DayCutoffMin = PopDay.IsLoaded ? (int)PopDay.Cutoff.TotalMinutes : null,
        };

        Func<DateTime, List<InjItemDailyDto>> summary = process switch
        {
            "IMG" => d => PopServices.ImgLots.GetDailyItemSummary(lineId, stationCode, d),
            "PNT" => d => PopServices.PntLots.GetDailyItemSummary(lineId, stationCode, d),
            _     => d => PopServices.InjLots.GetDailyItemSummary(lineId, stationCode, d),
        };
        var prodToday = PopDay.Today;   // 현황은 전기일 기준, today(달력 날짜)는 LotNo 헤더·번호 구간용
        for (var d = -past; d <= future; d++)
        {
            var day = prodToday.AddDays(d);
            next.Summaries[DayKey(day)] = summary(day);
        }

        next.Lots = process switch
        {
            "IMG" => PopServices.ImgLots.ListForOffline(lineId, since).Select(OfflineLot.From).ToList(),
            "PNT" => PopServices.PntLots.ListForOffline(lineId, since).Select(OfflineLot.From).ToList(),
            _     => PopServices.InjLots.ListForOffline(lineId, since).Select(OfflineLot.From).ToList(),
        };
        next.OpenWos = PopServices.WorkOrders.ListForLine(lineId);
        next.DefectCodes = PopServices.Defects.ListForProcess(process);
        next.Pallets = PopServices.Pallets.ListForOffline();
        OfflineAndon.Warm(lineId);

        var items = next.Summaries.Values.SelectMany(l => l).Select(i => i.ItemNo)
            .Concat(next.Lots.Select(l => l.ItemNo))
            .Concat(next.OpenWos.Select(w => w.StepItemNo ?? w.ItemNo))
            .Where(i => !string.IsNullOrEmpty(i))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        next.ItemRules = PopServices.Pallets.ReadItemRules(items);

        if (process == "INJ") PrefetchInj(next, lineId, items);
        else PrefetchFinished(next, process, lineId, items, today);

        // 오프라인 번호는 온라인일 때 DB 에서 구간을 받아 둔다(남은 번호가 모자랄 때만) — 이 PC 와 InjAgent 가 겹치지 않는다.
        var cursor = ReadCursor(lineId, today);
        var prior = (_stateLoaded ? _state : State)?.LotBlocks ?? new List<OfflineLotBlock>();
        var fresh = cursor is { } pc ? ReserveLotBlockIfShort(lineId, today, pc.Prefix, prior) : null;

        lock (Gate)
        {
            if (Ops.Ops.Count > 0) return;   // 받는 사이 오프라인 작업이 생겼다 — 덮지 않는다
            var old = _state is { } o && string.Equals(o.LineId, lineId, StringComparison.OrdinalIgnoreCase) ? o : null;
            next.LotPrefix = cursor?.Prefix ?? old?.LotPrefix ?? "";
            next.LotBlocks = (old?.LotBlocks ?? prior).Where(b => b.Day.Date == today.Date).ToList();
            if (fresh is not null) next.LotBlocks.Add(fresh);
            _state = next; _stateLoaded = true;
            SaveState(next);
        }
    }

    private static void PrefetchInj(OfflineWorkState next, string lineId, List<string> items)
    {
        foreach (var moldId in next.OpenWos.Select(w => w.MoldId).Where(m => !string.IsNullOrEmpty(m)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (PopServices.Molds.GetById(moldId!) is { } m) next.Molds[moldId!] = m;

        // 수동 라벨용 품번·금형·설비 정보 — 열린 WO 가 있으면 그 금형, 없으면 금형 없이(NULL)
        foreach (var item in items)
        {
            var moldId = next.OpenWos.FirstOrDefault(w => string.Equals(w.StepItemNo ?? w.ItemNo, item, StringComparison.OrdinalIgnoreCase))?.MoldId;
            var ctx = PopServices.InjLots.GetManualContext(lineId, item, moldId);
            next.Manual[ManualKey(moldId, item)] = new OfflineManualMold
            {
                ItemName = ctx.ItemName, MoldCode = ctx.MoldCode, ColorCode = ctx.ColorCode,
                CavityNo = ctx.CavityNo, CavityPos = ctx.CavityPos, EquipId = ctx.EquipId,
            };
        }
    }

    private static void PrefetchFinished(OfflineWorkState next, string process, string lineId, List<string> items, DateTime today)
    {
        var coreMap = process == "IMG" ? PopServices.ImgLots.ListCoreMap(lineId) : new();
        next.CoreMap = coreMap.Select(m => new OfflineCoreMap { WoId = m.WoId, FinishedItem = m.FinishedItem, CoreItem = m.CoreItem, CustomerCode = m.CustomerCode }).ToList();

        var labelItems = items.Concat(coreMap.Select(m => m.FinishedItem)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var item in labelItems)
        {
            var c = process == "IMG"
                ? Map(PopServices.ImgLots.GetLabelContext(lineId, item))
                : Map(PopServices.PntLots.GetLabelContext(lineId, item));
            next.Labels[item] = c;
        }

        if (process != "IMG") return;

        // 작업자가 완제품만 고르고 WO 가 없을 때 Core 스캔이 코어와 맞는지 보는 BOM 규칙
        foreach (var kv in PopServices.ImgLots.ReadBomCores(labelItems))
            next.BomCores[kv.Key] = new OfflineBomCore { Outcome = kv.Value.Outcome, CoreItemNo = kv.Value.CoreItemNo };

        // Core 후보 — 이 라인이 만들 수 있는 코어 품번(열린 WO 의 코어 + BOM 코어)만 담는다
        var relevant = coreMap.Select(m => m.CoreItem)
            .Concat(next.BomCores.Values.Where(b => b.Outcome == nameof(CoreItemResolver.Outcome.Core) && b.CoreItemNo is not null).Select(b => b.CoreItemNo!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        next.Cores = PopServices.ImgLots.ListCoresForOffline(today.AddDays(-CoreDays))
            .Where(c => relevant.Contains(c.ItemNo))
            .Select(c => new OfflineCore { LotCode = c.LotCode, ItemNo = c.ItemNo }).ToList();
    }

    private static OfflineLabel Map(ImgLotRepository.LabelContext c) => new()
    {
        Exists = c.ItemName is not null, ItemName = c.ItemName, Pgn = c.Pgn, Alc = c.Alc, MountPos = c.MountPos,
        CustomerCode = c.CustomerCode, EquipId = c.EquipId,
    };

    private static OfflineLabel Map(PntLotRepository.LabelContext c) => new()
    {
        Exists = c.ItemName is not null, ItemName = c.ItemName, Pgn = c.Pgn, Alc = c.Alc, MountPos = c.MountPos,
        CustomerCode = c.CustomerCode, EquipId = c.EquipId,
    };

    // 오늘 날짜는 남은 번호가 반이 안 되면 새 구간을 확보한다. 내일 날짜는 설정한 시각 이후에만 — 자정을 넘기는 정전만 대비하고
    // 평소에는 안 쓰고 버려질 번호(결번)를 만들지 않는다.
    /// <summary>오늘 헤더의 DB 마지막 순번 — 못 읽으면 null(이전 사본의 값을 유지한다).</summary>
    private static (string Prefix, string Header, int LastSeq)? ReadCursor(string lineId, DateTime today)
    {
        try { return PopServices.InjLots.ReadLotNoCursor(lineId, today); }
        catch (Exception ex) { PopLog.Append("offline", "OfflineWork", $"lot cursor read failed: {ex.Message}"); return null; }
    }

    /// <summary>오늘 헤더의 남은 번호가 모자라면 DB 에서 새 구간을 받는다. 실패하면 null — 이전 구간으로 간다.</summary>
    private static OfflineLotBlock? ReserveLotBlockIfShort(string lineId, DateTime today, string prefix, List<OfflineLotBlock> prior)
    {
        var header = LotNoGenerator.BuildHeader(today, prefix);
        var left = prior.Where(b => b.Header == header && b.Day.Date == today.Date).Sum(b => Math.Max(0, b.Last - b.Next + 1));
        if (left >= LotBlockRefillBelow) return null;
        try
        {
            var block = PopServices.InjLots.ReserveLotNos(lineId, today, LotBlockSize);
            return block.Count > 0
                ? new OfflineLotBlock(today.Date, block.Header, block.FirstSeq, block.FirstSeq + block.Count - 1)
                : null;
        }
        catch (Exception ex)
        {
            PopLog.Append("offline", "OfflineWork", $"lot block reserve failed: {ex.Message}");
            return null;
        }
    }

    private static string DayKey(DateTime d) => d.ToString("yyyy-MM-dd");
    private static string ManualKey(string? moldId, string itemNo) => $"{moldId ?? ""}|{itemNo}";

    // ── 온라인 중 덧붙이기 (5초 틱) ────────────────────────────────────

    /// <summary>
    /// 온라인 조회 결과를 사본에 덧붙인다 — 새로 생긴 LOT 을 명부에 올리고 상태를 서버 값으로 맞추며 오늘 현황을 갱신한다.
    /// 사본 전체를 새로 받는 주기(분)와 별개로 5초마다라서, 정전 직전에 만들어진 LOT 도 오프라인에서 알아본다.
    /// 반영 못 한 오프라인 작업이 있으면 하지 않는다. 디스크에는 1분에 한 번만 쓴다(<see cref="FlushIfDirty"/>).
    /// </summary>
    public static void Absorb(string lineId, string stationCode, IEnumerable<OfflineLot> lots, List<InjItemDailyDto>? todaySummary)
    {
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            if (s is null || Ops.Ops.Count > 0) return;
            if (!string.Equals(s.LineId, lineId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(s.StationCode, stationCode, StringComparison.OrdinalIgnoreCase)) return;

            var known = s.Lots.ToDictionary(l => l.LotCode, StringComparer.OrdinalIgnoreCase);
            foreach (var lot in lots)
            {
                if (known.TryGetValue(lot.LotCode, out var cur))
                {
                    cur.ConfirmStatus = lot.ConfirmStatus; cur.PrintedCount = lot.PrintedCount;
                    cur.OverallNg = lot.OverallNg; cur.InspectionSummary = lot.InspectionSummary; cur.ConfirmedAt = lot.ConfirmedAt;
                }
                else { s.Lots.Insert(0, lot); known[lot.LotCode] = lot; }
            }
            if (todaySummary is not null) s.Summaries[DayKey(PopDay.Today)] = todaySummary;
            _dirty = true;
        }
    }

    /// <summary>덧붙인 변경을 디스크에 쓴다 — 마지막으로 쓴 지 1분이 넘었을 때만(암호화 파일을 5초마다 쓰지 않으려고).</summary>
    public static void FlushIfDirty(bool force = false)
    {
        lock (Gate)
        {
            if (!_dirty || _state is not { } s) return;
            if (!force && DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastFlushTicks) < TimeSpan.FromMinutes(1).Ticks) return;
            SaveState(s);
        }
    }

    // ── 읽기 (오프라인 화면) ───────────────────────────────────────────

    /// <summary>그 날짜의 품번 현황 — 사본 값에 아직 반영 못 한 오프라인 작업의 효과를 얹는다.</summary>
    public static List<InjItemDailyDto> Summary(DateTime date)
    {
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            if (s is null) return new();
            var rows = s.Summaries.TryGetValue(DayKey(date), out var list) ? list : new();
            var effects = Ops.Ops
                .Where(o => o.LotDate.Date == date.Date)
                .GroupBy(o => o.ItemNo, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Aggregate((In: 0, Pend: 0, Conf: 0, Ng: 0), (a, o) =>
                {
                    var e = Effect(o);
                    return (a.In + e.In, a.Pend + e.Pend, a.Conf + e.Conf, a.Ng + e.Ng);
                }), StringComparer.OrdinalIgnoreCase);

            var result = new List<InjItemDailyDto>();
            foreach (var r in rows)
            {
                if (!effects.Remove(r.ItemNo, out var e)) { result.Add(r); continue; }
                result.Add(new InjItemDailyDto
                {
                    ItemNo = r.ItemNo, ItemName = r.ItemName, PlanQty = r.PlanQty,
                    InputQty = r.InputQty + e.In, PendingQty = r.PendingQty + e.Pend,
                    FinalQty = r.FinalQty + e.Conf, NgQty = r.NgQty + e.Ng,
                    InBop = r.InBop, HasOpenWo = r.HasOpenWo,
                });
            }
            // 사본 현황에 없던 품번(스캔 때 고른 품번)은 효과만으로 한 줄을 만든다
            foreach (var (item, e) in effects)
                result.Add(new InjItemDailyDto
                {
                    ItemNo = item, ItemName = ItemNameOf(s, item), PlanQty = 0, InputQty = e.In, PendingQty = e.Pend,
                    FinalQty = e.Conf, NgQty = e.Ng, InBop = false, HasOpenWo = FindOpenWo(s, item) is not null,
                });
            return result;
        }
    }

    private static (int In, int Pend, int Conf, int Ng) Effect(WorkOp op) => op.Kind switch
    {
        "ManualLot" or "CreateRaw" or "CreateFromCore" => (1, 1, 0, 0),
        "Confirm" => op.PriorStatus switch
        {
            LotDefectRules.Raw => (0, -1, 1, 0),
            "" => (1, 0, 1, 0),             // 사본에 없던 LOT — 오늘 새로 들어온 것으로 센다
            _ => (0, 0, 0, 0),
        },
        "Defect" => op.PriorStatus switch
        {
            LotDefectRules.Raw => (0, -1, 0, 1),
            LotDefectRules.Confirmed => (0, 0, -1, 1),
            "" => (1, 0, 0, 1),
            _ => (0, 0, 0, 0),              // NG_BLOCKED 는 이미 NG 로 센다
        },
        _ => (0, 0, 0, 0),
    };

    private static string ItemNameOf(OfflineWorkState s, string item)
        => s.Summaries.Values.SelectMany(l => l).FirstOrDefault(i => string.Equals(i.ItemNo, item, StringComparison.OrdinalIgnoreCase))?.ItemName
           ?? s.Lots.FirstOrDefault(l => string.Equals(l.ItemNo, item, StringComparison.OrdinalIgnoreCase))?.ItemName
           ?? s.OpenWos.FirstOrDefault(w => string.Equals(w.StepItemNo ?? w.ItemNo, item, StringComparison.OrdinalIgnoreCase))?.ItemName
           ?? (s.Labels.TryGetValue(item, out var lb) ? lb.ItemName : null)
           ?? "";

    private static WorkOrderDto? FindOpenWo(OfflineWorkState s, string? item)
        => item is null ? null
           : s.OpenWos.FirstOrDefault(w => string.Equals(w.StepItemNo ?? w.ItemNo, item, StringComparison.OrdinalIgnoreCase));

    public static WorkOrderDto? FindOpenWo(string? item)
    {
        var s = State;
        return s is null ? null : FindOpenWo(s, item);
    }

    public static string? WoNumber(int woId) => State?.OpenWos.FirstOrDefault(w => w.WoId == woId)?.WoNumber;

    public static List<DefectCodeDto> DefectCodes() => State?.DefectCodes ?? new();

    /// <summary>사본의 LOT 한 건 — 오프라인에서 바뀐 상태가 반영돼 있다.</summary>
    public static OfflineLot? FindLot(string lotCode)
    {
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            return s?.Lots.FirstOrDefault(l => string.Equals(l.LotCode, lotCode, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>MAIN 한 화면 분량 — 온라인 조회와 같은 모양으로 돌려준다.</summary>
    public static OfflineScreen? BuildScreen(string lineId, string stationCode, string? selItemNo, DateTime viewDate)
    {
        if (!HasData(lineId, stationCode)) return null;
        var s = State!;
        var items = Summary(viewDate);
        var today = Summary(PopDay.Today);

        if (!items.Any(x => string.Equals(x.ItemNo, selItemNo, StringComparison.OrdinalIgnoreCase)))
            selItemNo = (items.FirstOrDefault(x => x.HasOpenWo) ?? items.FirstOrDefault())?.ItemNo;

        var wo = FindOpenWo(s, selItemNo);
        MoldDto? mold = null;
        if (wo?.MoldId is { Length: > 0 } mid) s.Molds.TryGetValue(mid, out mold);

        var todayRow = today.FirstOrDefault(x => string.Equals(x.ItemNo, selItemNo, StringComparison.OrdinalIgnoreCase));
        List<OfflineLot> raw, todayLots; int rawCount, ngCount;
        lock (Gate)
        {
            var open = s.Lots.Where(l => l.ConfirmStatus is LotDefectRules.Raw or LotDefectRules.NgBlocked).ToList();
            raw = open.OrderByDescending(l => l.CreatedTS).Take(30).ToList();
            todayLots = s.Lots.Where(l => PopDay.Of(l.CreatedTS) == PopDay.Today).OrderByDescending(l => l.CreatedTS).Take(200).ToList();
            if (s.Process == "INJ")
            {
                rawCount = open.Count(l => l.ConfirmStatus == LotDefectRules.Raw);
                ngCount  = open.Count(l => l.ConfirmStatus == LotDefectRules.NgBlocked);
            }
            else { rawCount = todayLots.Count(l => l.ConfirmStatus == LotDefectRules.Raw); ngCount = 0; }
        }
        return new OfflineScreen(items, selItemNo, wo, todayRow?.FinalQty ?? 0, todayRow?.NgQty ?? 0, mold, raw, todayLots, rawCount, ngCount);
    }

    // ── 팔레트 ──────────────────────────────────────────────────────────

    /// <summary>사본에 있는 팔레트인가 — 아니면 오프라인에서는 검증 없이 받아들인다.</summary>
    public static bool IsKnownPallet(string palletCode)
    {
        var no = PalletLoadRules.NormalizePalletNo(palletCode) ?? "";
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            return s?.Pallets.Any(p => string.Equals(p.PalletNo, no, StringComparison.OrdinalIgnoreCase)) == true;
        }
    }

    public static bool IsUnverifiedPallet(string palletNo)
    {
        var no = PalletLoadRules.NormalizePalletNo(palletNo) ?? "";
        return State?.UnverifiedPallets.Contains(no, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// 사본의 팔레트로 판정한다(온라인 Inspect 와 같은 규칙). 사본에 없는 팔레트는 받아들인다 —
    /// 오프라인 중에 새로 발행됐을 수 있고, 맞는지는 복구 뒤 서버가 확정 때 판정한다.
    /// </summary>
    public static PalletStockRepository.PalletStatus Inspect(string palletCode, string? itemNo)
    {
        var no = PalletLoadRules.NormalizePalletNo(palletCode) ?? string.Empty;
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            var row = s?.Pallets.FirstOrDefault(p => string.Equals(p.PalletNo, no, StringComparison.OrdinalIgnoreCase));
            var state = row is null
                ? new PalletLoadRules.PalletState(true, true, false, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
                : new PalletLoadRules.PalletState(true, row.Active, row.Shipped, new Dictionary<string, int>(row.Loaded, StringComparer.OrdinalIgnoreCase));

            var item = itemNo;
            if (string.IsNullOrEmpty(item))
                item = state.Loaded.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key).FirstOrDefault();

            int? qty = null;
            IReadOnlySet<string> siblings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (item is not null)
            {
                if (s is not null && s.ItemRules.TryGetValue(item, out var rule))
                {
                    qty = rule.PalletQty;
                    siblings = new HashSet<string>(rule.Siblings, StringComparer.OrdinalIgnoreCase);
                }
                else siblings = new HashSet<string>(new[] { item }, StringComparer.OrdinalIgnoreCase);
            }
            var outcome = PalletLoadRules.Check(state, item ?? string.Empty, qty, siblings);
            return new PalletStockRepository.PalletStatus(no, state, item, qty, outcome);
        }
    }

    private static InjConfirmOutcome PalletOutcome(PalletLoadOutcome o) => o switch
    {
        PalletLoadOutcome.NotFound => InjConfirmOutcome.PalletNotFound,
        PalletLoadOutcome.Mixed    => InjConfirmOutcome.PalletMixed,
        PalletLoadOutcome.Full     => InjConfirmOutcome.PalletFull,
        _                          => InjConfirmOutcome.PalletUnavailable,
    };

    /// <summary>IMG·PNT 화면이 쓰는 확정 결과 — INJ 결과에서 이름이 같은 값을 옮긴다(IMG·PNT 는 NG_BLOCKED 가 없다).</summary>
    public static ImgConfirmOutcome ToImg(InjConfirmOutcome o)
        => Enum.TryParse<ImgConfirmOutcome>(o.ToString(), out var r) ? r : ImgConfirmOutcome.NotFound;

    // ── 쓰기 (오프라인 작업) ────────────────────────────────────────────

    /// <summary>사본에 없는 LOT 코드도 라벨에서 나온 모양이면 받아들인다 — 서버가 복구 뒤 판정한다.</summary>
    internal static bool LooksLikeLotCode(string code)
        => code.Length is >= 6 and <= 40 && !code.Contains(' ')
           && !code.StartsWith(PalletLoadRules.BarcodePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 스캔 확정·판정 OK(오프라인). 온라인 ConfirmByLotCode 와 같은 검사 순서:
    /// LOT 알아보기 → 라인 → 상태 → 팔레트. 통과하면 로컬 상태를 바꾸고 큐에 올린다. INJ·IMG·PNT 공용 — 사본의 공정이 반영 대상을 정한다.
    /// </summary>
    public static (InjConfirmOutcome Outcome, string ItemNo, int WoId, bool Unverified) Confirm(
        string lotCode, string lineId, string stationCode, string operatorId, string employeeNo, int sessionId,
        string palletNo, string? selectedItemNo)
    {
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            if (s is null) return (InjConfirmOutcome.NotFound, string.Empty, 0, false);

            var lot = s.Lots.FirstOrDefault(l => string.Equals(l.LotCode, lotCode, StringComparison.OrdinalIgnoreCase));
            string itemNo, prior = "";
            DateTime lotDate = PopDay.Today;
            if (lot is not null)
            {
                if (!string.Equals(lot.LineId, lineId, StringComparison.OrdinalIgnoreCase))
                    return (InjConfirmOutcome.WrongLine, lot.ItemNo, 0, false);
                switch (LotDefectRules.ConfirmBlock(lot.ConfirmStatus))
                {
                    case LotConfirmBlock.AlreadyConfirmed: return (InjConfirmOutcome.AlreadyConfirmed, lot.ItemNo, 0, false);
                    case LotConfirmBlock.NgBlocked:        return (InjConfirmOutcome.NgBlocked,        lot.ItemNo, 0, false);
                    case LotConfirmBlock.InRework:         return (InjConfirmOutcome.InRework,         lot.ItemNo, 0, false);
                    case LotConfirmBlock.Scrapped:         return (InjConfirmOutcome.Scrapped,         lot.ItemNo, 0, false);
                }
                itemNo = lot.ItemNo; prior = lot.ConfirmStatus; lotDate = PopDay.Of(lot.CreatedTS);
            }
            else
            {
                if (!LooksLikeLotCode(lotCode) || string.IsNullOrEmpty(selectedItemNo))
                    return (InjConfirmOutcome.NotFound, string.Empty, 0, false);
                itemNo = selectedItemNo;   // 사본에 없다 — 고른 품번으로 잠정 처리, 복구 뒤 서버가 LOT 의 실제 품번으로 판정
            }

            // 팔레트
            var no = PalletLoadRules.NormalizePalletNo(palletNo) ?? string.Empty;
            var pallet = s.Pallets.FirstOrDefault(p => string.Equals(p.PalletNo, no, StringComparison.OrdinalIgnoreCase));
            if (pallet is null)
            {
                pallet = new PalletStockRepository.OfflinePalletRow(no, true, false, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                s.Pallets.Add(pallet);
                if (!s.UnverifiedPallets.Contains(no, StringComparer.OrdinalIgnoreCase)) s.UnverifiedPallets.Add(no);
            }
            else
            {
                var state = new PalletLoadRules.PalletState(true, pallet.Active, pallet.Shipped, pallet.Loaded);
                IReadOnlySet<string> siblings = new HashSet<string>(new[] { itemNo }, StringComparer.OrdinalIgnoreCase);
                int? qty = null;
                if (s.ItemRules.TryGetValue(itemNo, out var rule))
                {
                    qty = rule.PalletQty;
                    siblings = new HashSet<string>(rule.Siblings, StringComparer.OrdinalIgnoreCase);
                }
                var check = PalletLoadRules.Check(state, itemNo, qty, siblings);
                if (check != PalletLoadOutcome.Ok) return (PalletOutcome(check), itemNo, 0, false);
            }

            // 반영: 팔레트 적재 수·LOT 상태·큐
            pallet.Loaded[itemNo] = (pallet.Loaded.TryGetValue(itemNo, out var n) ? n : 0) + 1;
            s.LotPallet[lotCode] = no;
            if (lot is not null) { lot.ConfirmStatus = LotDefectRules.Confirmed; lot.ConfirmedAt = DbClock.Now; }

            Ops.Ops.Add(new WorkOp
            {
                Process = s.Process, Kind = "Confirm", At = DbClock.Now, LineId = lineId, StationCode = stationCode, OperatorId = operatorId,
                EmployeeNo = employeeNo, SessionId = sessionId, LotCode = lotCode, ItemNo = itemNo, PalletNo = no,
                LotDate = lotDate, PriorStatus = prior,
            });
            SaveOps(); SaveState(s);
            return (InjConfirmOutcome.Confirmed, itemNo, FindOpenWo(s, itemNo)?.WoId ?? 0, lot is null || s.UnverifiedPallets.Contains(no, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>불량 등록·판정 NG(오프라인). 온라인 RegisterDefect 와 같은 검사(LotDefectRules.CheckRegister). INJ·IMG·PNT 공용.</summary>
    public static (DefectRegisterOutcome Outcome, string ItemNo, bool Unverified) RegisterDefect(
        string lotCode, string lineId, string stationCode, string operatorId, string employeeNo, int sessionId,
        string defectCode, string? selectedItemNo)
    {
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            if (s is null) return (DefectRegisterOutcome.NotFound, string.Empty, false);

            var lot = s.Lots.FirstOrDefault(l => string.Equals(l.LotCode, lotCode, StringComparison.OrdinalIgnoreCase));
            string itemNo, prior = "";
            DateTime lotDate = PopDay.Today;
            if (lot is not null)
            {
                if (!string.Equals(lot.LineId, lineId, StringComparison.OrdinalIgnoreCase))
                    return (DefectRegisterOutcome.WrongLine, lot.ItemNo, false);
                var check = LotDefectRules.CheckRegister(lot.ConfirmStatus);
                if (check != DefectRegisterOutcome.Registered) return (check, lot.ItemNo, false);
                itemNo = lot.ItemNo; prior = lot.ConfirmStatus; lotDate = PopDay.Of(lot.CreatedTS);
            }
            else
            {
                if (!LooksLikeLotCode(lotCode) || string.IsNullOrEmpty(selectedItemNo))
                    return (DefectRegisterOutcome.NotFound, string.Empty, false);
                itemNo = selectedItemNo;
            }

            // 오프라인에서 실은 LOT 이면 팔레트에서 다시 내린다(온라인 RegisterDefect 의 Unload 와 같은 뜻)
            if (s.LotPallet.Remove(lotCode, out var palletNo))
            {
                var p = s.Pallets.FirstOrDefault(x => string.Equals(x.PalletNo, palletNo, StringComparison.OrdinalIgnoreCase));
                if (p is not null && p.Loaded.TryGetValue(itemNo, out var n) && n > 0) p.Loaded[itemNo] = n - 1;
            }
            if (lot is not null) lot.ConfirmStatus = LotDefectRules.Defect;

            Ops.Ops.Add(new WorkOp
            {
                Process = s.Process, Kind = "Defect", At = DbClock.Now, LineId = lineId, StationCode = stationCode, OperatorId = operatorId,
                EmployeeNo = employeeNo, SessionId = sessionId, LotCode = lotCode, ItemNo = itemNo, DefectCode = defectCode,
                LotDate = lotDate, PriorStatus = prior,
            });
            SaveOps(); SaveState(s);
            return (DefectRegisterOutcome.Registered, itemNo, lot is null);
        }
    }

    // 온라인일 때 받아 둔 오늘 헤더의 구간에서 다음 번호를 받는다. 구간이 다 떨어졌으면 예외 — 온라인이 돌아와 구간을 다시 받아야 한다.
    private static string TakeLotNo(OfflineWorkState s, string lineId, DateTime now)
    {
        if (string.IsNullOrEmpty(s.LotPrefix)) throw new InvalidOperationException("LotPrefix missing");
        var header = LotNoGenerator.BuildHeader(now, s.LotPrefix);
        var i = s.LotBlocks.FindIndex(b => b.Header == header && b.Next <= b.Last);
        if (i < 0) throw new OfflineLotNumbersExhaustedException(lineId);
        var b = s.LotBlocks[i];
        s.LotBlocks[i] = b with { Next = b.Next + 1 };
        return header + b.Next.ToString("D4");
    }

    /// <summary>
    /// 수동 라벨(오프라인, INJ) — 미리 확보한 번호 구간에서 LOT 번호를 받아 LOT 을 만든다. 라벨 출력은 호출자가 하고
    /// <see cref="MarkPrinted"/> 로 횟수를 올린다(복구 뒤 라벨 디스패처가 또 뽑지 않게). 번호가 다 떨어졌으면 예외.
    /// </summary>
    public static OfflineLot CreateManualLot(
        string lineId, string stationCode, string operatorId, string employeeNo, int sessionId,
        string itemNo, string? moldId, int printed)
    {
        lock (Gate)
        {
            var s = (_stateLoaded ? _state : State) ?? throw new InvalidOperationException("offline data missing");

            var now = DbClock.Now;
            var lotCode = TakeLotNo(s, lineId, now);

            s.Manual.TryGetValue(ManualKey(moldId, itemNo), out var ctx);
            ctx ??= s.Manual.GetValueOrDefault(ManualKey(null, itemNo)) ?? new OfflineManualMold();

            var lot = new OfflineLot
            {
                LotId = -Interlocked.Increment(ref _syntheticLotId), LotCode = lotCode, ItemNo = itemNo,
                ItemName = ctx.ItemName ?? ItemNameOf(s, itemNo), LineId = lineId, EquipId = ctx.EquipId,
                MoldCode = ctx.MoldCode, ColorCode = ctx.ColorCode, MoldId = moldId,
                CavityNo = ctx.CavityNo, CavityPos = ctx.CavityPos, ConfirmStatus = LotDefectRules.Raw,
                PrintedCount = printed, CreatedTS = now,
            };
            s.Lots.Insert(0, lot);

            Ops.Ops.Add(new WorkOp
            {
                Process = s.Process, Kind = "ManualLot", At = now, LineId = lineId, StationCode = stationCode, OperatorId = operatorId,
                EmployeeNo = employeeNo, SessionId = sessionId, LotCode = lotCode, ItemNo = itemNo, MoldId = moldId,
                LotDate = PopDay.Of(now), PrintedCount = printed,
            });
            SaveOps(); SaveState(s);
            return lot;
        }
    }

    /// <summary>
    /// 예외용 라벨 발행(오프라인, IMG 발행 버튼·PNT 라벨 발행) — 고른 완제품 품번으로 RAW 완제품 LOT 1건. 번호는 미리 확보한 구간에서 받고
    /// 수주처·품번 정보는 사본의 라벨 정보를 쓴다. 라벨 출력 뒤 <see cref="MarkPrinted"/> 로 횟수를 올린다.
    /// </summary>
    public static OfflineLot CreateFinishedRaw(
        string lineId, string stationCode, string operatorId, string employeeNo, int sessionId, string itemNo)
    {
        lock (Gate)
        {
            var s = (_stateLoaded ? _state : State) ?? throw new InvalidOperationException("offline data missing");
            var now = DbClock.Now;
            var lotCode = TakeLotNo(s, lineId, now);
            var lot = NewFinishedLot(s, lotCode, lineId, itemNo, null, null, now);
            s.Lots.Insert(0, lot);

            Ops.Ops.Add(new WorkOp
            {
                Process = s.Process, Kind = "CreateRaw", At = now, LineId = lineId, StationCode = stationCode, OperatorId = operatorId,
                EmployeeNo = employeeNo, SessionId = sessionId, LotCode = lotCode, ItemNo = itemNo, CustomerCode = lot.CustomerCode,
                LotDate = PopDay.Of(now),
            });
            SaveOps(); SaveState(s);
            return lot;
        }
    }

    private static OfflineLot NewFinishedLot(OfflineWorkState s, string lotCode, string lineId, string itemNo, string? coreLotCode,
                                             string? customerOverride, DateTime now)
    {
        s.Labels.TryGetValue(itemNo, out var lb);
        return new OfflineLot
        {
            LotId = -Interlocked.Increment(ref _syntheticLotId), LotCode = lotCode, ItemNo = itemNo,
            ItemName = lb?.ItemName ?? ItemNameOf(s, itemNo), Pgn = lb?.Pgn, Alc = lb?.Alc, MountPos = lb?.MountPos,
            CustomerCode = customerOverride ?? lb?.CustomerCode, LineId = lineId, EquipId = lb?.EquipId,
            ConfirmStatus = LotDefectRules.Raw, PrintedCount = 0, CreatedTS = now, CoreLotCode = coreLotCode,
        };
    }

    /// <summary>
    /// Core 스캔(오프라인, IMG) — 온라인 CreateFromCore 와 같은 순서로 판정한다: 코어 알아보기·상태 → 이미 쓴 코어인지 →
    /// 완제품 결정(이 코어를 코어 단계로 갖는 열린 WO 의 완제품 → 없으면 작업자가 고른 품번 + BOM 코어 대조). 통과하면 번호를 받아 RAW LOT 을 만든다.
    /// 사본에 없는 코어는 받아들이되 고른 완제품이 있어야 하고 BOM 대조는 건너뛴다 — 서버가 복구 뒤 판정한다.
    /// </summary>
    public static (ImgCoreOutcome Outcome, OfflineLot? Lot, string? UsedBy, string? ItemNo, bool Unverified) CreateFromCore(
        string coreLotCode, string lineId, string stationCode, string operatorId, string employeeNo, int sessionId,
        string? selectedFinishedItem)
    {
        lock (Gate)
        {
            var s = (_stateLoaded ? _state : State) ?? throw new InvalidOperationException("offline data missing");

            var core = s.Cores.FirstOrDefault(c => string.Equals(c.LotCode, coreLotCode, StringComparison.OrdinalIgnoreCase));
            if (s.UsedCores.Contains(coreLotCode, StringComparer.OrdinalIgnoreCase))
            {
                var usedBy = s.Lots.FirstOrDefault(l => string.Equals(l.CoreLotCode, coreLotCode, StringComparison.OrdinalIgnoreCase))?.LotCode;
                return (ImgCoreOutcome.CoreUsed, null, usedBy, core?.ItemNo, false);
            }
            var unverified = core is null;
            if (unverified && (!LooksLikeLotCode(coreLotCode) || string.IsNullOrEmpty(selectedFinishedItem)))
                return (ImgCoreOutcome.NotFound, null, null, null, false);

            // 완제품 결정
            string finishedItem; string? customer = null; var customerFromWo = false;
            var coreItem = core?.ItemNo;
            var wo = coreItem is null ? null
                : s.CoreMap.FirstOrDefault(m => string.Equals(m.CoreItem, coreItem, StringComparison.OrdinalIgnoreCase)
                                                && (string.IsNullOrEmpty(selectedFinishedItem)
                                                    || string.Equals(m.FinishedItem, selectedFinishedItem, StringComparison.OrdinalIgnoreCase)));
            if (wo is not null) { finishedItem = wo.FinishedItem; customer = wo.CustomerCode; customerFromWo = true; }
            else
            {
                if (string.IsNullOrEmpty(selectedFinishedItem)
                    || !s.Labels.TryGetValue(selectedFinishedItem, out var lb) || !lb.Exists)
                    return (ImgCoreOutcome.NoFinishedItem, null, null, coreItem, false);
                // BOM 이 코어를 정할 수 있는 품번이면 스캔한 코어와 맞아야 한다. 못 정하는 품번은 선택을 믿는다.
                if (coreItem is not null && s.BomCores.TryGetValue(selectedFinishedItem, out var bom)
                    && bom.Outcome == nameof(CoreItemResolver.Outcome.Core)
                    && !string.Equals(bom.CoreItemNo, coreItem, StringComparison.OrdinalIgnoreCase))
                    return (ImgCoreOutcome.CoreMismatch, null, null, coreItem, false);
                finishedItem = selectedFinishedItem;
            }

            var now = DbClock.Now;
            var lotCode = TakeLotNo(s, lineId, now);
            var lot = NewFinishedLot(s, lotCode, lineId, finishedItem, coreLotCode, customerFromWo ? customer : null, now);
            s.Lots.Insert(0, lot);
            if (core is not null) s.Cores.Remove(core);
            s.UsedCores.Add(coreLotCode);

            Ops.Ops.Add(new WorkOp
            {
                Process = s.Process, Kind = "CreateFromCore", At = now, LineId = lineId, StationCode = stationCode, OperatorId = operatorId,
                EmployeeNo = employeeNo, SessionId = sessionId, LotCode = lotCode, ItemNo = finishedItem, CoreLotCode = coreLotCode,
                FinishedItem = selectedFinishedItem, CustomerCode = lot.CustomerCode, LotDate = PopDay.Of(now),
            });
            SaveOps(); SaveState(s);
            return (ImgCoreOutcome.Created, lot, null, finishedItem, unverified);
        }
    }

    /// <summary>
    /// 오프라인 LOT 의 라벨이 나왔다 — 발행 횟수를 올린다. 복구 때 이 횟수로 LOT 을 넣어야 라벨 디스패처가 같은 LOT 을 또 뽑지 않는다.
    /// 서버에 이미 있던 LOT 의 재출력은 해당 없다(횟수는 서버가 센다).
    /// </summary>
    public static void MarkPrinted(string lotCode)
    {
        lock (Gate)
        {
            var s = _stateLoaded ? _state : State;
            var op = Ops.Ops.FirstOrDefault(o => o.Kind is "ManualLot" or "CreateRaw" or "CreateFromCore"
                                                 && string.Equals(o.LotCode, lotCode, StringComparison.OrdinalIgnoreCase));
            if (s is null || op is null) return;
            op.PrintedCount++;
            var lot = s.Lots.FirstOrDefault(l => string.Equals(l.LotCode, lotCode, StringComparison.OrdinalIgnoreCase));
            if (lot is not null) lot.PrintedCount = op.PrintedCount;
            SaveOps(); SaveState(s);
        }
    }

    // ── 복구 때 반영 ────────────────────────────────────────────────────

    /// <summary>5초 틱에서 — 살아 있으면 큐를 반영하고, 로그인 중이면 사본이 오래됐을 때 새로 받는다.</summary>
    public static void Tick(AppState app)
    {
        if (PopDb.IsOffline) return;
        FlushIfDirty();
        if (PendingCount > 0) { Replay(); return; }
        if (app.Session is not { } s || OfflineAuth.ProcessOf(s.LineId) is not { } process) return;
        switch (process)
        {
            case "INJ" or "IMG" or "PNT": PrefetchInBackground(s.LineId, s.TerminalId, process); break;
            case OfflineRework.Process:   OfflineReads.WarmInBackground(process, OfflineRework.Warm); break;
            case OfflineQc.Process:       OfflineReads.WarmInBackground(process, OfflineQc.Warm); break;
        }
    }

    public static void Replay()
    {
        if (PopDb.IsOffline) return;
        if (Interlocked.CompareExchange(ref _replaying, 1, 0) != 0) return;
        int done = 0, conflicts = 0;
        var deferred = new HashSet<WorkOp>();
        try
        {
            while (true)
            {
                WorkOp? op;
                lock (Gate) op = Ops.Ops.FirstOrDefault(o => !deferred.Contains(o));
                if (op is null) break;

                // 오프라인 로그인으로 만든 임시 세션 ID 는 인증 저널이 실제 세션을 만든 뒤에야 바꿀 수 있다
                int? session = op.SessionId;
                if (op.SessionId < 0)
                {
                    session = OfflineAuth.MappedSession(op.SessionId);
                    if (session is null && OfflineAuth.PendingCount > 0) break;   // 아직 세션 반영 전 — 다음 틱
                }

                try
                {
                    var reason = Apply(op, session);
                    if (reason is not null)
                    {
                        // "없는 LOT"은 다른 PC 의 에이전트가 아직 못 올린 LOT 일 수 있다 — 정해진 시간 동안은 큐에 두고 다시 시도한다
                        if (IsRetryable(reason) && DbClock.Now - op.At < TimeSpan.FromMinutes(NotFoundRetryMinutes))
                        {
                            deferred.Add(op);
                            continue;
                        }
                        conflicts++;
                        AddConflict(op, reason);
                    }
                    done++;
                }
                catch (Exception ex)
                {
                    if (DbHealth.IsConnectionFailure(ex) || PopDb.IsOffline)
                    {
                        PopLog.Append("offline", "OfflineWork", $"replay paused at {op.Kind} {op.LotCode}: {ex.Message}");
                        break;
                    }
                    conflicts++; done++;
                    AddConflict(op, $"error: {ex.Message}");
                }
                lock (Gate) { Ops.Ops.Remove(op); SaveOps(); }
            }
        }
        finally { Interlocked.Exchange(ref _replaying, 0); }

        if (done > 0)
            Notice?.Invoke(conflicts == 0
                ? $"오프라인 작업 {done}건을 서버에 반영했습니다."
                : $"오프라인 작업 {done}건 반영 — 충돌 {conflicts}건 (확인 필요)", conflicts > 0);
    }

    private static bool IsRetryable(string reason) => reason is "NotFound" or "CoreUnconfirmed";

    // 충돌이면 사유, 정상(또는 이미 반영돼 있어 무해)이면 null.
    private static string? Apply(WorkOp op, int? session)
    {
        switch (op.Process)
        {
            case OfflineAndon.Process:  return OfflineAndon.Apply(op);
            case OfflineRework.Process: return OfflineRework.Apply(op, session);
            case OfflineQc.Process:     return OfflineQc.Apply(op);
        }
        switch (op.Process, op.Kind)
        {
            // ── INJ ──
            case ("INJ", "ManualLot"):
                // 이미 있으면(재전송) 새로 만든 것이 없다 — 무해
                PopServices.InjLots.CreateManualRawLotsWithCodes(op.LineId, op.ItemNo, op.MoldId, op.EmployeeNo,
                    new[] { (op.LotCode, op.At, op.PrintedCount) });
                return null;

            case ("INJ", "Confirm"):
            {
                var (outcome, _, _, _) = PopServices.InjLots.ConfirmByLotCode(
                    op.LotCode, op.LineId, op.OperatorId, session, op.EmployeeNo, op.PalletNo, op.At, out _);
                return outcome is InjConfirmOutcome.Confirmed or InjConfirmOutcome.AlreadyConfirmed ? null : outcome.ToString();
            }

            case ("INJ", "Defect"):
            {
                var (outcome, _, _) = PopServices.InjLots.RegisterDefect(
                    op.LotCode, op.LineId, op.DefectCode ?? "", op.OperatorId, session, op.EmployeeNo, op.At, out _);
                return outcome is DefectRegisterOutcome.Registered or DefectRegisterOutcome.AlreadyInRework ? null : outcome.ToString();
            }

            // ── IMG ──
            case ("IMG", "CreateFromCore"):
            {
                var (outcome, _, usedBy, _) = PopServices.ImgLots.CreateFromCoreWithCode(
                    op.CoreLotCode ?? "", op.LineId, op.EmployeeNo, op.FinishedItem, op.LotCode, op.At, op.PrintedCount, op.CustomerCode);
                return outcome == ImgCoreOutcome.Created ? null : outcome == ImgCoreOutcome.CoreUsed ? $"CoreUsed ({usedBy})" : outcome.ToString();
            }

            case ("IMG", "CreateRaw"):
                PopServices.ImgLots.CreateRawLotWithCode(op.LineId, op.ItemNo, op.EmployeeNo, op.LotCode, op.At, op.PrintedCount, op.CustomerCode);
                return null;

            case ("IMG", "Confirm"):
            {
                var (outcome, _, _, _) = PopServices.ImgLots.ConfirmByLotCode(
                    op.LotCode, op.LineId, op.OperatorId, session, op.EmployeeNo, op.PalletNo, op.At, out _);
                return outcome is ImgConfirmOutcome.Confirmed or ImgConfirmOutcome.AlreadyConfirmed ? null : outcome.ToString();
            }

            case ("IMG", "Defect"):
            {
                var (outcome, _, _) = PopServices.ImgLots.RegisterDefect(
                    op.LotCode, op.LineId, op.DefectCode ?? "", op.OperatorId, session, op.EmployeeNo, op.At, out _);
                return outcome is DefectRegisterOutcome.Registered or DefectRegisterOutcome.AlreadyInRework ? null : outcome.ToString();
            }

            // ── PNT ──
            case ("PNT", "CreateRaw"):
                PopServices.PntLots.CreateRawLotWithCode(op.LineId, op.ItemNo, op.EmployeeNo, op.LotCode, op.At, op.PrintedCount, op.CustomerCode);
                return null;

            case ("PNT", "Confirm"):
            {
                var (outcome, _, _, _) = PopServices.PntLots.ConfirmByLotCode(
                    op.LotCode, op.LineId, op.OperatorId, session, op.EmployeeNo, op.PalletNo, op.At, out _);
                return outcome is ImgConfirmOutcome.Confirmed or ImgConfirmOutcome.AlreadyConfirmed ? null : outcome.ToString();
            }

            case ("PNT", "Defect"):
            {
                var (outcome, _, _) = PopServices.PntLots.RegisterDefect(
                    op.LotCode, op.LineId, op.DefectCode ?? "", op.OperatorId, session, op.EmployeeNo, op.At, out _);
                return outcome is DefectRegisterOutcome.Registered or DefectRegisterOutcome.AlreadyInRework ? null : outcome.ToString();
            }
        }
        return $"unknown op {op.Process}/{op.Kind}";
    }

    private static void AddConflict(WorkOp op, string reason)
    {
        PopLog.Append("offline", "OfflineWork", $"conflict {op.Process} {op.Kind} {op.LotCode} @ {op.At:MM-dd HH:mm}: {reason}");
        lock (Gate)
        {
            Ops.Conflicts.Add(new WorkConflict { At = op.At, Kind = op.Kind, LotCode = op.LotCode, Reason = reason });
            if (Ops.Conflicts.Count > 200) Ops.Conflicts.RemoveRange(0, Ops.Conflicts.Count - 200);
        }
    }
}

/// <summary>오프라인에서 새 LOT 번호가 다 떨어졌다 — 온라인일 때 미리 확보해 둔 구간을 모두 썼다.</summary>
internal sealed class OfflineLotNumbersExhaustedException : InvalidOperationException
{
    public OfflineLotNumbersExhaustedException(string lineId)
        : base($"offline LOT numbers exhausted for {lineId}") { }
}
