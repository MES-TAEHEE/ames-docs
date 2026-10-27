using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Common;
using AMES.Pop.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>IMG·PNT 오프라인 큐가 복구 뒤 DB 에 반영되는지. AMES_TEST_CONN(기본 개발 DB)이 닿을 때만 돈다. 먼 미래(2036-01-05) 번호만 쓰고 끝에 지운다.</summary>
[Collection("OfflineAuth")]
public sealed class OfflineReplayFinishedDbTests : IDisposable
{
    private const string InjLine = "LINE-INJ-09", ImgLine = "LINE-IMG-05", PntLine = "LINE-PNT-02", Station = "ST-ITEST";
    private static readonly DateTime FarDay = new(2036, 1, 5);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-replayfin-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;
    private readonly AmesConnectionFactory? _factory;

    private static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.0.132,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

    public OfflineReplayFinishedDbTests()
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

    private object? Scalar(string sql, params (string, object)[] ps)
    {
        using var conn = _factory!.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    private void Clean()
    {
        foreach (var line in new[] { InjLine, ImgLine, PntLine })
        {
            var h = Header(line);
            using var conn = _factory!.OpenConnection();
            using var cmd = new SqlCommand("""
                DECLARE @ids TABLE (LotID int);
                INSERT @ids SELECT LotID FROM dbo.tbl_Lot WHERE LotCode LIKE @H + '%';
                DELETE dbo.PR_DefectDetail WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_ProductionResult WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_InjLot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_ImgLot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_PntLot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.tbl_Lot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.SYS_LotSeq WHERE Header = @H;
                """, conn);
            cmd.Parameters.AddWithValue("@H", h);
            cmd.ExecuteNonQuery();
        }
    }

    private string Header(string line) => LotNoGenerator.BuildHeader(FarDay, ((string)Scalar("SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L", ("@L", line))!).Trim());

    private string NewCode(string line) => new InjLotRepository(_factory!).ReserveLotNos(line, FarDay, 1).Codes.Single();

    private static void Seed(string process, string line)
    {
        var s = new OfflineWorkState
        {
            Process = process, TakenAtUtc = DateTime.UtcNow, LineId = line, StationCode = Station, PastDays = 7, FutureDays = 7,
            Summaries = new() { [DateTime.Today.ToString("yyyy-MM-dd")] = new() },
        };
        OfflineStore.Save("work-state.bin", s);
        OfflineWork.ResetForTests();
    }

    private static WorkOp Op(string process, string kind, string line, string lot, string item, DateTime at) => new()
    {
        Process = process, Kind = kind, At = at, LineId = line, StationCode = Station, OperatorId = "ITEST", EmployeeNo = "ITEST", SessionId = -1,
        LotCode = lot, ItemNo = item, LotDate = FarDay,
    };

    private static void SaveOps(params WorkOp[] ops)
    {
        OfflineStore.Save("work-ops.bin", new WorkOpsFile { Ops = ops.ToList() });
        OfflineWork.ResetForTests();
    }

    private string? FinishedItemTrustedBySelection()
    {
        using var conn = _factory!.OpenConnection();
        var names = new List<string>();
        using (var cmd = new SqlCommand("SELECT TOP 80 ItemNo FROM dbo.MD_Item WHERE ISNULL(ActiveFlag,1) = 1 ORDER BY ItemNo", conn))
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add((string)r[0]);
        return names.FirstOrDefault(n => CoreItemResolver.ReadByInjFlag(conn, null, n).Outcome != CoreItemResolver.Outcome.Core);
    }

    [SkippableFact]
    public void Pnt_label_issue_judgement_ok_and_defect_replay_with_their_original_times()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var codeA = NewCode(PntLine);
        var codeB = NewCode(PntLine);
        Seed("PNT", PntLine);
        var t = new DateTime(2036, 1, 5, 9, 0, 0);

        var create = (Op("PNT", "CreateRaw", PntLine, codeA, "ITEST-FIN", t)); create.PrintedCount = 1; create.CustomerCode = "CUST-P";
        var confirm = Op("PNT", "Confirm", PntLine, codeA, "ITEST-FIN", t.AddMinutes(2)); confirm.PriorStatus = "RAW";
        var createB = Op("PNT", "CreateRaw", PntLine, codeB, "ITEST-FIN", t.AddMinutes(3)); createB.PrintedCount = 1;
        var defect = Op("PNT", "Defect", PntLine, codeB, "ITEST-FIN", t.AddMinutes(5)); defect.DefectCode = "ITEST-D01"; defect.PriorStatus = "RAW";
        SaveOps(create, confirm, createB, defect);

        OfflineWork.Replay();

        Assert.Equal(0, OfflineWork.PendingCount);
        Assert.True(OfflineWork.Conflicts.Count == 0, string.Join("; ", OfflineWork.Conflicts.Select(c => $"{c.Kind} {c.LotCode} {c.Reason}")));
        Assert.Equal(t, Scalar("SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", codeA)));
        Assert.Equal("CUST-P", Scalar("SELECT e.CustomerCode FROM dbo.PR_PntLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codeA)));
        Assert.Equal(1, Scalar("SELECT e.PrintedCount FROM dbo.PR_PntLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codeA)));
        Assert.Equal("CONFIRMED", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_PntLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codeA)));
        Assert.Equal(t.AddMinutes(2), Scalar("SELECT r.EntryAt FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C AND r.GoodQty > 0", ("@C", codeA)));
        Assert.Equal("DEFECT", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_PntLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", codeB)));
        Assert.Equal(t.AddMinutes(5), Scalar("SELECT d.DetectedAt FROM dbo.PR_DefectDetail d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID WHERE l.LotCode = @C", ("@C", codeB)));
    }

    [SkippableFact]
    public void Img_core_scan_judgement_ok_and_defect_replay_in_order()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var finished = FinishedItemTrustedBySelection();
        Skip.If(finished is null, "no suitable finished item in this DB");

        var inj = new InjLotRepository(_factory!);
        var coreCode = NewCode(InjLine);
        var t = new DateTime(2036, 1, 5, 9, 30, 0);
        inj.CreateManualRawLotsWithCodes(InjLine, "ITEST-CORE", null, "ITEST", new[] { (coreCode, t.AddMinutes(-10), 0) });
        Assert.Equal(InjConfirmOutcome.Confirmed, inj.ConfirmByLotCode(coreCode, InjLine, "ITEST", null, "ITEST", null, t.AddMinutes(-9), out _).Outcome);

        var imgCode = NewCode(ImgLine);
        Seed("IMG", ImgLine);
        var scan = Op("IMG", "CreateFromCore", ImgLine, imgCode, finished!, t);
        scan.CoreLotCode = coreCode; scan.FinishedItem = finished; scan.PrintedCount = 1; scan.CustomerCode = "CUST-I";
        var judge = Op("IMG", "Confirm", ImgLine, imgCode, finished!, t.AddMinutes(1)); judge.PriorStatus = "RAW";
        var ng = Op("IMG", "Defect", ImgLine, imgCode, finished!, t.AddMinutes(4)); ng.DefectCode = "ITEST-D01"; ng.PriorStatus = "CONFIRMED";
        SaveOps(scan, judge, ng);

        OfflineWork.Replay();

        Assert.Equal(0, OfflineWork.PendingCount);
        Assert.True(OfflineWork.Conflicts.Count == 0, string.Join("; ", OfflineWork.Conflicts.Select(c => $"{c.Kind} {c.LotCode} {c.Reason}")));
        Assert.Equal(t, Scalar("SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", imgCode)));
        Assert.Equal(coreCode, Scalar("SELECT p.LotCode FROM dbo.tbl_Lot l JOIN dbo.tbl_Lot p ON p.LotID = l.ParentLotID WHERE l.LotCode = @C", ("@C", imgCode)));
        Assert.Equal("CUST-I", Scalar("SELECT e.CustomerCode FROM dbo.PR_ImgLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", imgCode)));
        Assert.Equal(1, Scalar("SELECT e.PrintedCount FROM dbo.PR_ImgLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", imgCode)));
        // 확정 뒤 불량: LOT 은 DEFECT, 확정 실적과 그 역분개 실적이 각자 시각으로 남는다
        Assert.Equal("DEFECT", Scalar("SELECT e.ConfirmStatus FROM dbo.PR_ImgLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", imgCode)));
        Assert.Equal(t.AddMinutes(1), Scalar("SELECT r.EntryAt FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C AND r.GoodQty > 0", ("@C", imgCode)));
        Assert.Equal(t.AddMinutes(4), Scalar("SELECT r.EntryAt FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C AND r.GoodQty < 0", ("@C", imgCode)));
    }

    [SkippableFact]
    public void A_core_the_server_does_not_have_yet_is_retried_then_applied_when_it_arrives()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        var finished = FinishedItemTrustedBySelection();
        Skip.If(finished is null, "no suitable finished item in this DB");

        var inj = new InjLotRepository(_factory!);
        var coreCode = NewCode(InjLine);      // 번호만 받고 LOT 은 아직 서버에 없다 — 다른 PC 의 에이전트가 올리기 전
        var imgCode = NewCode(ImgLine);
        Seed("IMG", ImgLine);
        var scan = Op("IMG", "CreateFromCore", ImgLine, imgCode, finished!, DbClock.Now.AddMinutes(-1));
        scan.CoreLotCode = coreCode; scan.FinishedItem = finished; scan.PrintedCount = 1;
        SaveOps(scan);

        OfflineWork.Replay();
        Assert.Equal(1, OfflineWork.PendingCount);   // 충돌로 보지 않고 큐에 둔다
        Assert.Empty(OfflineWork.Conflicts);

        // 코어가 서버에 올라왔다 — 다음 틱에 정상 반영
        var at = new DateTime(2036, 1, 5, 9, 0, 0);
        inj.CreateManualRawLotsWithCodes(InjLine, "ITEST-CORE", null, "ITEST", new[] { (coreCode, at, 0) });
        Assert.Equal(InjConfirmOutcome.Confirmed, inj.ConfirmByLotCode(coreCode, InjLine, "ITEST", null, "ITEST", null, at, out _).Outcome);
        OfflineWork.Replay();

        Assert.Equal(0, OfflineWork.PendingCount);
        Assert.Empty(OfflineWork.Conflicts);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", imgCode)));
    }
}
