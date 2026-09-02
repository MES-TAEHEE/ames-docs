using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// WO 생성 시 품목 RoutingType 필수 규칙. AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// </summary>
public class WorkOrderRepositoryTests
{
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "Server=192.168.2.137,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;";

    const string ItemNoRouting = "ITEST-WO-NORT";
    const string ItemRoutingA  = "ITEST-WO-RTA";

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

    static void Exec(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    static void SeedItems(AmesConnectionFactory f)
    {
        CleanupItems(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy)
            VALUES (@A, N'ITEST no routing', NULL, 1, 'ITEST'),
                   (@B, N'ITEST routing A',  'A',  1, 'ITEST');
            """, ("@A", ItemNoRouting), ("@B", ItemRoutingA));
    }

    static void CleanupItems(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo IN (@A, @B);
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo IN (@A, @B);
            DELETE FROM dbo.MD_Item          WHERE ItemNo IN (@A, @B);
            """, ("@A", ItemNoRouting), ("@B", ItemRoutingA));
    }

    [SkippableFact]
    public void CreateManualWo_rejects_item_without_routing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);

            var wo = repo.CreateManualWo(ItemNoRouting, 10, DateTime.Today.AddDays(7), "itest");

            Assert.Equal(string.Empty, wo);
            var n = (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE ItemNo = @I;", ("@I", ItemNoRouting))!;
            Assert.Equal(0, n);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void CreateManualWo_copies_routing_from_item()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);

            var wo = repo.CreateManualWo(ItemRoutingA, 10, DateTime.Today.AddDays(7), "itest");

            Assert.NotEqual(string.Empty, wo);
            var rt = Scalar(f, "SELECT RoutingType FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", wo)) as string;
            Assert.Equal("A", rt);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void CreateWorkOrdersForOrders_skips_item_without_routing_and_copies_routing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var soNoRouting = (int)Scalar(f, """
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
                OUTPUT INSERTED.SoID
                VALUES ('SO-ITEST-NORT', 1, @I, 50, DATEADD(day, 7, CAST(GETDATE() AS date)), 'Confirmed', 'ITEST');
                """, ("@I", ItemNoRouting))!;
            var soRoutingA = (int)Scalar(f, """
                INSERT INTO dbo.PP_CustomerOrder (SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status, CreatedBy)
                OUTPUT INSERTED.SoID
                VALUES ('SO-ITEST-RTA', 1, @I, 50, DATEADD(day, 7, CAST(GETDATE() AS date)), 'Confirmed', 'ITEST');
                """, ("@I", ItemRoutingA))!;

            var repo    = new PpRepository(f);
            var created = repo.CreateWorkOrdersForOrders(new[] { soNoRouting, soRoutingA }, "itest", useNetReq: false);

            Assert.Single(created);
            var n = (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE SoID = @S;", ("@S", soNoRouting))!;
            Assert.Equal(0, n);
            var rt = Scalar(f, "SELECT RoutingType FROM dbo.PP_WorkOrder WHERE SoID = @S;", ("@S", soRoutingA)) as string;
            Assert.Equal("A", rt);
        }
        finally { CleanupItems(f); }
    }
}
