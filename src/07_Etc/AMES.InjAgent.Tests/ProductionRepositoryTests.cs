using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// 로컬/원격 AMES_DEV 통합 테스트. DB 미기동 시 skip — InjLotRepositoryTests 와 같은 방식.
/// </summary>
public class ProductionRepositoryTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.1.137,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

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

    [SkippableFact]
    public void RecordCycle_uses_9char_rule()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new ProductionRepository(f);

        int woId;
        using (var conn = f.OpenConnection())
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.WoID
            VALUES ('WO-ITEST-IMGCYC', '83335-P8000RBQ', 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
            """, conn))
            woId = (int)cmd.ExecuteScalar()!;

        using (var conn = f.OpenConnection())
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
            VALUES (@W, 1, 'IMG', 'LINE-IMG-01', 'In Progress', 0, 'ITEST');
            """, conn))
        { cmd.Parameters.AddWithValue("@W", woId); cmd.ExecuteNonQuery(); }

        int resultId = 0, lotId = 0;
        try
        {
            decimal newCompleted;
            (resultId, lotId, newCompleted) = repo.RecordCycle(
                woId, "83335-P8000RBQ", "LINE-IMG-01", "IMG",
                goodQty: 10, cycleSec: 30, moldId: null,
                operatorId: "itest", sessionId: null, employeeNo: "E-TEST", defectFlag: false);

            Assert.Equal(10m, newCompleted);

            using (var conn2 = f.OpenConnection())
            using (var cmd2 = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 1;", conn2))
            {
                cmd2.Parameters.AddWithValue("@W", woId);
                Assert.Equal(10m, (decimal)cmd2.ExecuteScalar()!);
            }

            using var conn = f.OpenConnection();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT LotCode FROM dbo.tbl_Lot WHERE LotID = @L;", conn);
            cmd.Parameters.AddWithValue("@L", lotId);
            var lotCode = (string)cmd.ExecuteScalar()!;
            Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]W1\d{4}$", lotCode);
        }
        finally
        {
            using var conn = f.OpenConnection();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;
                DELETE FROM dbo.PR_ProductionResult WHERE ResultID = @R;
                DELETE FROM dbo.tbl_Lot WHERE LotID = @L;
                DELETE FROM dbo.PP_WorkOrder WHERE WoID = @W;
                """, conn);
            cmd.Parameters.AddWithValue("@R", resultId);
            cmd.Parameters.AddWithValue("@L", lotId);
            cmd.Parameters.AddWithValue("@W", woId);
            cmd.ExecuteNonQuery();
        }
    }
}
