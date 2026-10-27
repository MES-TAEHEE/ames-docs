using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Common;
using AMES.Pop.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>
/// 오프라인에서 쌓은 작업이 복구 뒤 DB 에 제대로 반영되는지 — 큐 → 저장소 → 행. AMES_TEST_CONN(기본 개발 DB)이 닿을 때만 돈다.
/// 먼 미래 날짜(2036-01-05) 번호만 써서 실제 데이터와 섞이지 않고 끝에 지운다.
/// </summary>
[Collection("OfflineAuth")]
public sealed class OfflineReplayDbTests : IDisposable
{
    private const string Line = "LINE-INJ-09", Station = "ST-ITEST", Item = "ITEST-OFFLINE";
    private static readonly DateTime FarDay = new(2036, 1, 5);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-replay-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;
    private readonly AmesConnectionFactory? _factory;

    private static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.0.132,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    public OfflineReplayDbTests()
    {
        OfflineStore.Dir = _dir;
        OfflineWork.ResetForTests();
        OfflineAuth.ResetForTests();
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using (f.OpenConnection()) { }
            _factory = f;
            PopServices.Initialize(Conn);
            Clean();
        }
        catch { _factory = null; }
    }

    public void Dispose()
    {
        if (_factory is not null) Clean();
        OfflineStore.Dir = _origDir;
        OfflineWork.ResetForTests();
        OfflineAuth.ResetForTests();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Header()
    {
        using var conn = _factory!.OpenConnection();
        using var cmd = new SqlCommand("SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L", conn);
        cmd.Parameters.AddWithValue("@L", Line);
        return LotNoGenerator.BuildHeader(FarDay, ((string)cmd.ExecuteScalar()!).Trim());
    }

    private void Exec(string sql, params (string, object)[] ps)
    {
        using var conn = _factory!.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql, params (string, object)[] ps)
    {
        using var conn = _factory!.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    private void Clean()
    {
        var h = Header();
        Exec("""
            DECLARE @ids TABLE (LotID int);
            INSERT @ids SELECT LotID FROM dbo.tbl_Lot WHERE LotCode LIKE @H + '%';
            DELETE dbo.PR_DefectDetail WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.PR_ProductionResult WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.PR_InjLot WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.tbl_Lot WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.SYS_LotSeq WHERE Header = @H;
            """, ("@H", h));
    }

    private static void Seed(params OfflineLot[] lots)
    {
        var s = new OfflineWorkState
        {
            Process = "INJ", TakenAtUtc = DateTime.UtcNow, LineId = Line, StationCode = Station, PastDays = 7, FutureDays = 7,
            Lots = lots.ToList(),
            Summaries = new() { [DateTime.Today.ToString("yyyy-MM-dd")] = new() },
            Manual = new() { ["|" + Item] = new OfflineManualMold { ItemName = "x" } },
        };
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();
    }

    private OfflineLot DbLot(string code, DateTime at)
    {
        // 온라인에서 만들어져 서버에 이미 있는 LOT — 사본에 담겨 있다
        new InjLotRepository(_factory!).CreateManualRawLotsWithCodes(Line, Item, null, "ITEST", new[] { (code, at, 0) });
        return new OfflineLot { LotId = 1, LotCode = code, ItemNo = Item, LineId = Line, ConfirmStatus = "RAW", CreatedTS = at };
    }

    [SkippableFact]
    public void Replay_applies_manual_lot_confirm_and_defect_in_order_with_their_original_times()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(_factory!);
        var block = repo.ReserveLotNos(Line, FarDay, 3);
        var codes = block.Codes.ToArray();
        var existing = DbLot(codes[0], new DateTime(2036, 1, 5, 8, 0, 0));
        Seed(existing);

        // 사본의 번호 구간 — 오프라인에서 새 라벨이 이 번호를 쓴다 (먼 미래 날짜라 평소 채번과 섞이지 않는다)
        var s = OfflineWork.State!;
        s.LotPrefix = (block.Header)[3..5];
        s.LotBlocks.Add(new OfflineLotBlock(FarDay, block.Header, block.FirstSeq + 1, block.FirstSeq + block.Count - 1));
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();

        // 오프라인 작업: 기존 LOT 확정, 새 수동 LOT 발행(+출력), 그 LOT 불량 등록. CreateManualLot 은 "지금" 날짜 번호를 찾으므로
        // 발생 시각은 큐 항목을 직접 고쳐 먼 미래로 맞춘다.
        Assert.Equal(InjConfirmOutcome.Confirmed,
            OfflineWork.Confirm(codes[0], Line, Station, "ITEST", "ITEST", -1, "EOS-PLT-NONE", Item).Outcome);

        var opsFile = OfflineStore.Load<WorkOpsFile>("work-ops.bin")!;
        // 팔레트 반영(재고 적재)은 별도 시험 대상 — 여기서는 팔레트 없이 확정으로 시각·순서만 본다
        foreach (var op in opsFile.Ops) { op.At = new DateTime(2036, 1, 5, 9, 15, 0); op.LotDate = FarDay; op.PalletNo = null; }
        // 새 수동 LOT 과 그 불량 — 큐에 직접 넣는다 (번호는 예약한 두 번째 번호)
        opsFile.Ops.Add(new WorkOp { Kind = "ManualLot", At = new DateTime(2036, 1, 5, 9, 20, 0), LineId = Line, StationCode = Station, OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1, LotCode = codes[1], ItemNo = Item, LotDate = FarDay, PrintedCount = 1 });
        opsFile.Ops.Add(new WorkOp { Kind = "Defect", At = new DateTime(2036, 1, 5, 9, 25, 0), LineId = Line, StationCode = Station, OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1, LotCode = codes[1], ItemNo = Item, DefectCode = "ITEST-D01", LotDate = FarDay, PriorStatus = "RAW" });
        OfflineStore.Save("work-ops.bin", opsFile);
        OfflineWork.ResetForTests();
        Assert.Equal(3, OfflineWork.PendingCount);

        string? notice = null;
        OfflineWork.Notice += (t, _) => notice = t;
        OfflineWork.Replay();

        Assert.Equal(0, OfflineWork.PendingCount);
        Assert.True(OfflineWork.Conflicts.Count == 0, string.Join("; ", OfflineWork.Conflicts.Select(c => $"{c.Kind} {c.LotCode} {c.Reason}")));
        Assert.NotNull(notice);

        // 확정: 그 시각으로 실적이 남고 LOT 이 CONFIRMED
        Assert.Equal(new DateTime(2036, 1, 5, 9, 15, 0),
            Scalar("SELECT r.EntryAt FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C AND r.GoodQty > 0", ("@C", codes[0])));
        Assert.Equal("CONFIRMED", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codes[0])));

        // 수동 LOT: 예약한 번호·생성 시각·출력 횟수 그대로, 이어서 불량 등록으로 DEFECT
        Assert.Equal(new DateTime(2036, 1, 5, 9, 20, 0), Scalar("SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", codes[1])));
        Assert.Equal(1, Scalar("SELECT e.PrintedCount FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codes[1])));
        Assert.Equal("DEFECT", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codes[1])));
        Assert.Equal(new DateTime(2036, 1, 5, 9, 25, 0),
            Scalar("SELECT d.DetectedAt FROM dbo.PR_DefectDetail d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID WHERE l.LotCode = @C", ("@C", codes[1])));
    }

    [SkippableFact]
    public void A_pallet_missing_on_the_server_is_rejected_after_recovery_and_the_lot_stays_raw()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var code = new InjLotRepository(_factory!).ReserveLotNos(Line, FarDay, 1).Codes.Single();
        var lot = DbLot(code, new DateTime(2036, 1, 5, 8, 0, 0));
        Seed(lot);

        // 오프라인에서는 사본에 없는 팔레트도 받아들였다
        var r = OfflineWork.Confirm(code, Line, Station, "ITEST", "ITEST", -1, "EOS-PLT-NONE", Item);
        Assert.Equal(InjConfirmOutcome.Confirmed, r.Outcome);
        Assert.True(r.Unverified);

        OfflineWork.Replay();

        // 복구 뒤 서버는 팔레트가 없다고 거부 — 확정되지 않고 충돌 목록에 남아 관리자가 처리한다
        var conflict = Assert.Single(OfflineWork.Conflicts);
        Assert.Equal("PalletNotFound", conflict.Reason);
        Assert.Equal(code, conflict.LotCode);
        Assert.Equal("RAW", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
        Assert.Equal(0, OfflineWork.PendingCount);
    }

    [SkippableFact]
    public void Server_rejections_become_conflicts_and_never_block_the_rest_of_the_queue()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(_factory!);
        var codes = repo.ReserveLotNos(Line, FarDay, 2).Codes.ToArray();
        var good = DbLot(codes[0], new DateTime(2036, 1, 5, 8, 0, 0));
        Seed(good);

        var ops = new WorkOpsFile();
        // 서버에 없는 LOT 을 오프라인에서 받아들였던 것 — 복구 뒤 NotFound. 재시도 시간(30분)이 지난 건이라 충돌이 된다
        ops.Ops.Add(new WorkOp { Kind = "Confirm", At = AMES.Data.Services.DbClock.Now.AddHours(-2), LineId = Line, StationCode = Station, OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1, LotCode = Header() + "9998", ItemNo = Item, PalletNo = null, LotDate = FarDay });
        // 정상 건 — 앞의 충돌이 이 건을 막지 않는다
        ops.Ops.Add(new WorkOp { Kind = "Confirm", At = new DateTime(2036, 1, 5, 9, 5, 0), LineId = Line, StationCode = Station, OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1, LotCode = codes[0], ItemNo = Item, PalletNo = null, LotDate = FarDay, PriorStatus = "RAW" });
        // 같은 LOT 을 또 확정 — AlreadyConfirmed 는 무해(재전송)라 충돌이 아니다
        ops.Ops.Add(new WorkOp { Kind = "Confirm", At = new DateTime(2036, 1, 5, 9, 6, 0), LineId = Line, StationCode = Station, OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1, LotCode = codes[0], ItemNo = Item, PalletNo = null, LotDate = FarDay });
        OfflineStore.Save("work-ops.bin", ops);
        OfflineWork.ResetForTests();

        OfflineWork.Replay();

        Assert.Equal(0, OfflineWork.PendingCount);
        var conflict = Assert.Single(OfflineWork.Conflicts);
        Assert.Equal("NotFound", conflict.Reason);
        Assert.EndsWith("9998", conflict.LotCode);
        Assert.Equal("CONFIRMED", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codes[0])));
    }

    [SkippableFact]
    public void A_lot_the_server_does_not_know_yet_is_retried_before_it_becomes_a_conflict()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(_factory!);
        var codes = repo.ReserveLotNos(Line, FarDay, 1).Codes.ToArray();
        Seed();   // 사본에는 아무 LOT 도 없다

        // 다른 PC 의 에이전트가 아직 못 올린 LOT 을 오프라인에서 스캔해 받아들였다 — 방금 일어난 일
        var ops = new WorkOpsFile();
        ops.Ops.Add(new WorkOp { Kind = "Confirm", At = AMES.Data.Services.DbClock.Now.AddMinutes(-1), LineId = Line, StationCode = Station,
            OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1, LotCode = codes[0], ItemNo = Item, LotDate = FarDay });
        OfflineStore.Save("work-ops.bin", ops);
        OfflineWork.ResetForTests();

        OfflineWork.Replay();
        Assert.Equal(1, OfflineWork.PendingCount);     // 충돌로 보지 않고 큐에 둔다
        Assert.Empty(OfflineWork.Conflicts);

        // 그 사이 LOT 이 서버에 올라왔다 — 다음 틱에 정상 반영
        new InjLotRepository(_factory!).CreateManualRawLotsWithCodes(Line, Item, null, "ITEST", new[] { (codes[0], new DateTime(2036, 1, 5, 8, 0, 0), 0) });
        OfflineWork.Replay();
        Assert.Equal(0, OfflineWork.PendingCount);
        Assert.Empty(OfflineWork.Conflicts);
        Assert.Equal("CONFIRMED", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codes[0])));
    }

    [SkippableFact]
    public void Replay_waits_for_the_temporary_session_to_be_mapped_before_using_it()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var codes = new InjLotRepository(_factory!).ReserveLotNos(Line, FarDay, 1).Codes.ToArray();
        Seed(DbLot(codes[0], new DateTime(2036, 1, 5, 8, 0, 0)));

        // 오프라인 로그인으로 만든 임시 세션(-5) + 아직 서버에 못 올린 인증 기록이 남아 있다 → 작업은 기다린다
        var cacheFile = new OfflineAuthCache { SyncedAtUtc = DateTime.UtcNow, Employees = new() { new CachedEmployee("ITEST", "n", "u-itest", "Active", 0, null, true, false, null) } };
        OfflineStore.Save("auth-cache.bin", cacheFile);
        OfflineAuth.ResetForTests();
        OfflineAuth.Login(new AMES.Contracts.Auth.LoginRequest { AttemptedId = "ITEST", Method = AuthMethod.Badge, TerminalId = Station, LineId = Line, ShiftCode = "A" });
        Assert.True(OfflineAuth.PendingCount > 0);

        var ops = new WorkOpsFile();
        ops.Ops.Add(new WorkOp { Kind = "Confirm", At = new DateTime(2036, 1, 5, 9, 0, 0), LineId = Line, StationCode = Station, OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1, LotCode = codes[0], ItemNo = Item, LotDate = FarDay, PriorStatus = "RAW" });
        OfflineStore.Save("work-ops.bin", ops);
        OfflineWork.ResetForTests();

        OfflineWork.Replay();

        Assert.Equal(1, OfflineWork.PendingCount);   // 세션 매핑 전이라 그대로 남는다
        Assert.Equal("RAW", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codes[0])));
    }
}
