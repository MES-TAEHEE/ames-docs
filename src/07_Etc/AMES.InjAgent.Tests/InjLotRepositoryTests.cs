using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// 로컬 AMES_DEV(Docker) 통합 테스트. DB 미기동 시 각 테스트가 skip 된다.
/// Task 1 마이그레이션 + 시드가 선행되어야 한다.
/// </summary>
public class InjLotRepositoryTests
{
    // 개발 DB 는 원격(appsettings.Development.json 과 같은 서버). 다른 서버로 돌릴 땐 AMES_TEST_CONN 으로 덮어쓴다.
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.1.100,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

    static AmesConnectionFactory? TryFactory()
    {
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using var c = f.OpenConnection();
            return f;
        }
        catch { return null; }
    }

    static MoldItemDto Lh() => new()
    { MoldCode = "LQ2DTMD", ColorCode = "CBK", CavityNo = 1, CavityPos = "LH", ItemNo = "83335-P8000RBQ", MoldId = null };

    static void Cleanup(AmesConnectionFactory f, int lotId)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            DELETE FROM dbo.PR_RobotInspection WHERE LotID = @L;
            DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
            DELETE FROM dbo.tbl_Lot WHERE LotID = @L;
            """, conn);
        cmd.Parameters.AddWithValue("@L", lotId);
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public void GetMoldItems_returns_seeded_two_cavity_map()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var map = repo.GetMoldItems("LQ2DTMD", "CBK");
        Assert.Equal(2, map.Count);
        Assert.Equal("LH", map[0].CavityPos);
        Assert.Equal("RH", map[1].CavityPos);
    }

    [SkippableFact]
    public void CreateRawLot_then_appears_in_unconfirmed_list()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 12345);
        try
        {
            Assert.True(lotId > 0);
            var list = repo.GetUnconfirmed("LINE-INJ-01");
            Assert.Contains(list, x => x.LotId == lotId && x.ConfirmStatus == "RAW" && x.LotCode == lotCode);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void IncrementPrintedCount_accumulates_and_shows_in_view()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 12347);
        try
        {
            Assert.Equal(1, repo.IncrementPrintedCount(lotId));   // 디스패처 자동 발행
            Assert.Equal(2, repo.IncrementPrintedCount(lotId));   // 재출력 버튼
            Assert.Equal(2, repo.GetByLotCode(lotCode)!.PrintedCount);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void MarkNgBlocked_then_confirm_is_rejected()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 12346);
        try
        {
            repo.SaveInspection(lotId, "INJ-650-01", "LH", "OK", "OK", "OK", "NG", overallNg: true);
            repo.MarkNgBlocked(lotId);
            var (outcome, _, _, _) = repo.ConfirmByLotCode(lotCode, "LINE-INJ-01", "test-op", null, "E-TEST");
            Assert.Equal(InjConfirmOutcome.NgBlocked, outcome);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void Confirm_unknown_lot_returns_notfound()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (outcome, _, _, _) = repo.ConfirmByLotCode("L-NO-SUCH-LOT", "LINE-INJ-01", "test-op", null, "E-TEST");
        Assert.Equal(InjConfirmOutcome.NotFound, outcome);
    }

    [SkippableFact]
    public void Confirm_raw_lot_creates_result_and_bumps_wo()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        int woId = 0, lotId = 0;
        try
        {
            // 테스트 전용 WO 생성
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('WO-ITEST-CONFIRM', '83335-P8000RBQ', 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
                """, conn))
                woId = (int)cmd.ExecuteScalar()!;

            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
                VALUES (@W, 1, 'INJ', 'LINE-INJ-01', 'In Progress', 0, 'ITEST');
                """, conn))
            { cmd.Parameters.AddWithValue("@W", woId); cmd.ExecuteNonQuery(); }

            string lotCode;
            (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 99001);

            // woId 는 더 이상 전달하지 않는다 — 리포지토리가 LOT 품번으로 WO 를 찾아야 한다.
            var (outcome, resultId, itemNo, confirmedWoId) = repo.ConfirmByLotCode(
                lotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");

            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            Assert.True(resultId > 0);
            Assert.Equal("83335-P8000RBQ", itemNo);
            Assert.Equal(woId, confirmedWoId);   // 품번 매칭으로 테스트 WO 가 선택됐는지

            // 실적/상태/수량 검증
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.PR_ProductionResult WHERE ResultID = @R AND LotID = @L AND GoodQty = 1) AS ResultOk,
                  (SELECT ConfirmStatus FROM dbo.PR_InjLot WHERE LotID = @L) AS Status,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 1) AS StepCompleted,
                  (SELECT Status FROM dbo.tbl_Lot WHERE LotID = @L) AS LotStatus;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@R", resultId);
                cmd.Parameters.AddWithValue("@L", lotId);
                cmd.Parameters.AddWithValue("@W", woId);
                using var rdr = cmd.ExecuteReader();
                Assert.True(rdr.Read());
                Assert.Equal(1, (int)rdr["ResultOk"]);
                Assert.Equal("CONFIRMED", (string)rdr["Status"]);
                Assert.Equal(1m, (decimal)rdr["Completed"]);
                Assert.Equal(1m, (decimal)rdr["StepCompleted"]);
                Assert.Equal("CONFIRMED", (string)rdr["LotStatus"]);
            }

            // 중복 스캔 거부
            var (again, _, _, _) = repo.ConfirmByLotCode(lotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");
            Assert.Equal(InjConfirmOutcome.AlreadyConfirmed, again);
        }
        finally
        {
            if (f is not null)
            {
                using var conn = f.OpenConnection();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                    DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;
                    DELETE FROM dbo.PR_ProductionResult WHERE LotID = @L;
                    DELETE FROM dbo.PR_RobotInspection WHERE LotID = @L;
                    DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
                    DELETE FROM dbo.tbl_Lot WHERE LotID = @L;
                    DELETE FROM dbo.PP_WorkOrder WHERE WoID = @W;
                    """, conn);
                cmd.Parameters.AddWithValue("@L", lotId);
                cmd.Parameters.AddWithValue("@W", woId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// 수동 발행은 에이전트와 같은 원천 LOT(RAW) 만 만든다 — 1 LOT = 1 PCS, 실적/WO 수량은 건드리지 않고
    /// 금형 타수도 움직이지 않는다. 확정은 오직 스캔(ConfirmByLotCode) 경유.
    /// </summary>
    [SkippableFact]
    public void CreateManualRawLots_issues_raw_lots_only_and_confirms_by_scan()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);

        string? moldId;
        using (var conn = f!.OpenConnection())
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT TOP 1 MoldID FROM dbo.MD_Mold WHERE MoldCodeClean = 'LQ2DTMD';", conn))
            moldId = cmd.ExecuteScalar() as string;
        Skip.If(moldId is null, "seed_inj_demo not applied");

        // CurrentShots 는 INT, CumulativeShots 는 BIGINT — 둘 다 long 으로 읽는다.
        (long Cur, long Cum) ReadShots()
        {
            using var conn = f!.OpenConnection();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT ISNULL(CurrentShots,0) AS C, ISNULL(CumulativeShots,0) AS M FROM dbo.MD_Mold WHERE MoldID = @M;", conn);
            cmd.Parameters.AddWithValue("@M", moldId);
            using var rdr = cmd.ExecuteReader();
            rdr.Read();
            return (Convert.ToInt64(rdr["C"]), Convert.ToInt64(rdr["M"]));
        }

        const int qty = 3;
        var woId = 0;
        var lotIds = new List<int>();
        try
        {
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('WO-ITEST-MANUAL', '83335-P8000RBQ', 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
                """, conn))
                woId = (int)cmd.ExecuteScalar()!;

            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
                VALUES (@W, 1, 'INJ', 'LINE-INJ-01', 'In Progress', 0, 'ITEST');
                """, conn))
            { cmd.Parameters.AddWithValue("@W", woId); cmd.ExecuteNonQuery(); }

            var shotsBefore = ReadShots();

            var lots = repo.CreateManualRawLots("LINE-INJ-01", "83335-P8000RBQ", moldId, qty, "E-ITEST");
            lotIds.AddRange(lots.Select(l => l.LotId));

            Assert.Equal(qty, lots.Count);
            Assert.Equal(qty, lots.Select(l => l.LotCode).Distinct().Count());   // LotCode 중복 없음
            Assert.All(lots, l => Assert.Equal("RAW", l.ConfirmStatus));
            Assert.All(lots, l => Assert.Equal("LH", l.CavityPos));              // 품번 → 캐비티 매핑
            Assert.All(lots, l => Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", l.LotCode!));

            // 발행 직후: 원천 LOT 만 있고 실적·WO 수량은 그대로여야 한다.
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.tbl_Lot
                    WHERE LotID IN (SELECT value FROM STRING_SPLIT(@Ids, ','))
                      AND BatchSize = 1 AND RemainingQty = 1 AND WoID IS NULL
                      AND Status = 'RAW' AND QualityFlag = 'PENDING' AND ProcessCode = 'INJ') AS Lots,
                  (SELECT COUNT(*) FROM dbo.PR_InjLot
                    WHERE LotID IN (SELECT value FROM STRING_SPLIT(@Ids, ','))
                      AND ConfirmStatus = 'RAW' AND CreatedBy = 'MANUAL'
                      AND MachineShotCount IS NULL AND ConfirmedAt IS NULL AND CavityPos = 'LH') AS InjLots,
                  (SELECT COUNT(*) FROM dbo.PR_ProductionResult
                    WHERE LotID IN (SELECT value FROM STRING_SPLIT(@Ids, ','))) AS Results,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@Ids", string.Join(",", lotIds));
                cmd.Parameters.AddWithValue("@W", woId);
                using var rdr = cmd.ExecuteReader();
                Assert.True(rdr.Read());
                Assert.Equal(qty, (int)rdr["Lots"]);
                Assert.Equal(qty, (int)rdr["InjLots"]);
                Assert.Equal(0, (int)rdr["Results"]);        // 아직 실적 아님
                Assert.Equal(0m, (decimal)rdr["Completed"]); // WO 수량도 그대로
            }

            Assert.Equal(shotsBefore, ReadShots());   // 타수는 PLC 샷카운터 전용

            // 발행분은 미확정 목록에 뜬다.
            var unconfirmed = repo.GetUnconfirmed("LINE-INJ-01");
            Assert.All(lots, l => Assert.Contains(unconfirmed, x => x.LotId == l.LotId));

            // 스캔해야 비로소 실적이 된다.
            var (outcome, resultId, _, confirmedWoId) = repo.ConfirmByLotCode(
                lots[0].LotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");
            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            Assert.True(resultId > 0);
            Assert.Equal(woId, confirmedWoId);

            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT (SELECT Status FROM dbo.tbl_Lot WHERE LotID = @L) AS LotStatus,
                       (SELECT WoID   FROM dbo.tbl_Lot WHERE LotID = @L) AS LotWo,
                       (SELECT COUNT(*) FROM dbo.PR_ProductionResult WHERE LotID = @L AND GoodQty = 1) AS Results,
                       (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@L", lots[0].LotId);
                cmd.Parameters.AddWithValue("@W", woId);
                using var rdr = cmd.ExecuteReader();
                Assert.True(rdr.Read());
                Assert.Equal("CONFIRMED", (string)rdr["LotStatus"]);
                Assert.Equal(woId, (int)rdr["LotWo"]);       // 확정 시점에 WO 가 붙는다
                Assert.Equal(1, (int)rdr["Results"]);
                Assert.Equal(1m, (decimal)rdr["Completed"]); // 스캔한 1건만 반영
            }

            Assert.Equal(shotsBefore, ReadShots());   // 스캔 확정도 타수를 올리지 않는다(에이전트 샷 전용)
        }
        finally
        {
            // 발행 직후 LOT 은 WoID 가 NULL 이라 WO 기준만으로는 지워지지 않는다 — LotID 로도 지운다.
            if (f is not null && woId > 0)
            {
                using var conn = f.OpenConnection();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                    DECLARE @L TABLE (LotID INT);
                    INSERT INTO @L SELECT CAST(value AS INT) FROM STRING_SPLIT(@Ids, ',') WHERE value <> '';
                    INSERT INTO @L SELECT LotID FROM dbo.tbl_Lot
                      WHERE WoID = @W AND LotID NOT IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;
                    DELETE FROM dbo.PR_ProductionResult WHERE WoID = @W OR LotID IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.PR_InjLot          WHERE LotID IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.tbl_Lot            WHERE LotID IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.PP_WorkOrder       WHERE WoID = @W;
                    """, conn);
                cmd.Parameters.AddWithValue("@Ids", string.Join(",", lotIds));
                cmd.Parameters.AddWithValue("@W", woId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    [SkippableFact]
    public void NextLotNo_increments_within_header_and_rolls_new_header()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        using var tx = conn.BeginTransaction();

        var d1 = new DateTime(2026, 9, 1);
        var a = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1);
        var b = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1);
        Assert.Equal(9, a.Length);
        Assert.Equal(a[..5], b[..5]);
        Assert.Equal(int.Parse(a[5..]) + 1, int.Parse(b[5..]));

        var c = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1.AddDays(1));
        Assert.NotEqual(a[..5], c[..5]);   // 날짜가 바뀌면 새 헤더

        tx.Rollback();   // 카운터도 롤백 — 테스트 흔적 없음
    }

    [SkippableFact]
    public void NextLotNo_line_without_prefix_throws()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        using var tx = conn.BeginTransaction();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-NOPREFIX", DateTime.Now));
        Assert.Contains("LotPrefix", ex.Message);
        tx.Rollback();
    }

    [SkippableFact]
    public void CreateRawLot_uses_9char_rule_with_incrementing_seq()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (id1, c1) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 90001);
        var (id2, c2) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 90002);
        try
        {
            Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", c1);
            Assert.Equal(c1[..5], c2[..5]);
            Assert.Equal(int.Parse(c1[5..]) + 1, int.Parse(c2[5..]));
        }
        finally { Cleanup(f, id1); Cleanup(f, id2); }
    }

    [SkippableFact]
    public void CreateManualRawLots_consecutive_seq_and_9char_rule()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var lots = repo.CreateManualRawLots("LINE-INJ-01", "83335-P8000RBQ", null, 3, "E-TEST");
        try
        {
            Assert.Equal(3, lots.Count);
            Assert.All(lots, l => Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", l.LotCode!));
            var seqs = lots.Select(l => int.Parse(l.LotCode![5..])).ToList();
            Assert.Equal(seqs[0] + 1, seqs[1]);
            Assert.Equal(seqs[1] + 1, seqs[2]);
        }
        finally { foreach (var l in lots) Cleanup(f, l.LotId); }
    }

    [SkippableFact]
    public void CreateManualRawLots_line_without_prefix_throws_and_rolls_back()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            repo.CreateManualRawLots("LINE-NOPREFIX", "83335-P8000RBQ", null, 1, "E-TEST"));
        Assert.Contains("LotPrefix", ex.Message);
    }

    [SkippableFact]
    public void CreateRawLot_parallel_yields_unique_codes()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var results = new System.Collections.Concurrent.ConcurrentBag<(int LotId, string LotCode)>();
        Parallel.For(0, 8, i =>
            results.Add(repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 91000 + i)));
        try
        {
            Assert.Equal(8, results.Count);
            Assert.Equal(8, results.Select(r => r.LotCode).Distinct().Count());
        }
        finally { foreach (var (id, _) in results) Cleanup(f, id); }
    }
}
