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
    const string Conn = "Server=localhost,1433;Database=AMES_DEV;User Id=sa;Password=AmesDev!2026Sa;TrustServerCertificate=True;Encrypt=True;Connect Timeout=3;";

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
            Assert.Equal(1, repo.IncrementPrintedCount(lotId));   // 에이전트 최초 발행
            Assert.Equal(2, repo.IncrementPrintedCount(lotId));   // Inj04 재출력
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
            var (outcome, _, _) = repo.ConfirmByLotCode(lotCode, "LINE-INJ-01", woId: 0, "test-op", null, "E-TEST");
            Assert.Equal(InjConfirmOutcome.NgBlocked, outcome);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void Confirm_unknown_lot_returns_notfound()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (outcome, _, _) = repo.ConfirmByLotCode("L-NO-SUCH-LOT", "LINE-INJ-01", 0, "test-op", null, "E-TEST");
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
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, LineID, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('WO-ITEST-CONFIRM', '83335-P8000RBQ', 'LINE-INJ-01', 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
                """, conn))
                woId = (int)cmd.ExecuteScalar()!;

            string lotCode;
            (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 99001);

            var (outcome, resultId, itemNo) = repo.ConfirmByLotCode(
                lotCode, "LINE-INJ-01", woId, "itest-op", null, "E-ITEST");

            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            Assert.True(resultId > 0);
            Assert.Equal("83335-P8000RBQ", itemNo);

            // 실적/상태/수량 검증
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.PR_ProductionResult WHERE ResultID = @R AND LotID = @L AND GoodQty = 1) AS ResultOk,
                  (SELECT ConfirmStatus FROM dbo.PR_InjLot WHERE LotID = @L) AS Status,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed,
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
                Assert.Equal("CONFIRMED", (string)rdr["LotStatus"]);
            }

            // 중복 스캔 거부
            var (again, _, _) = repo.ConfirmByLotCode(lotCode, "LINE-INJ-01", woId, "itest-op", null, "E-ITEST");
            Assert.Equal(InjConfirmOutcome.AlreadyConfirmed, again);
        }
        finally
        {
            if (f is not null)
            {
                using var conn = f.OpenConnection();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
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
}
