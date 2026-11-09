using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>품번 BOM 미리보기의 현재 재고 요약(<see cref="WarehouseRepository.GetItemStockSummary"/>) — 읽기 전용, AMES_TEST_CONN 지정 시.</summary>
public class ItemStockSummaryTests
{
    [SkippableFact]
    public void Summary_matches_inventory_rows_for_items_with_locations_and_backflush_shortage()
    {
        var conn = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(conn), "Set AMES_TEST_CONN for read-only stock summary verification.");
        var factory = new AmesConnectionFactory(conn!);
        var wh = new WarehouseRepository(factory);

        // 위치가 가장 많이 나뉜 품번과 음수(자동 차감 부족)가 있는 품번을 골라 원본 행과 대조한다
        var partNos = new List<string>();
        using (var c = factory.OpenConnection())
        using (var cmd = new SqlCommand("""
            SELECT TOP 1 PartNo FROM dbo.WH_Inventory WHERE Qty > 0 AND PartNo IS NOT NULL
            GROUP BY PartNo ORDER BY COUNT(DISTINCT ISNULL(LocationNo, '')) DESC, PartNo;
            SELECT TOP 1 PartNo FROM dbo.WH_Inventory WHERE Qty < 0 AND PartNo IS NOT NULL GROUP BY PartNo ORDER BY PartNo;
            """, c))
        using (var r = cmd.ExecuteReader())
        {
            do { while (r.Read()) partNos.Add(r.GetString(0)); } while (r.NextResult());
        }
        Skip.If(partNos.Count == 0, "No inventory rows to compare.");

        foreach (var p in partNos.Distinct())
        {
            var s = wh.GetItemStockSummary(p);
            using var c = factory.OpenConnection();
            using var cmd = new SqlCommand("""
                SELECT SUM(CASE WHEN Qty > 0 THEN Qty ELSE 0 END),
                       SUM(CASE WHEN Qty < 0 THEN Qty ELSE 0 END),
                       COUNT(CASE WHEN Qty > 0 THEN 1 END),
                       SUM(CASE WHEN Qty > 0 AND NULLIF(LTRIM(RTRIM(LocationNo)), '') IS NULL THEN Qty ELSE 0 END),
                       COUNT(DISTINCT CASE WHEN Qty > 0 THEN NULLIF(LTRIM(RTRIM(LocationNo)), '') END)
                FROM dbo.WH_Inventory WHERE PartNo = @P
                """, c);
            cmd.Parameters.Add("@P", System.Data.SqlDbType.VarChar, 20).Value = p;
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(r.IsDBNull(0) ? 0 : r.GetDecimal(0), s.OnHand);
            Assert.Equal(r.IsDBNull(1) ? 0 : r.GetDecimal(1), s.Shortage);
            Assert.Equal(r.GetInt32(2), s.Lots);
            Assert.Equal(r.IsDBNull(3) ? 0 : r.GetDecimal(3), s.Unassigned);
            Assert.Equal(r.GetInt32(4), s.Locations.Count);
            Assert.Equal(s.OnHand, s.Unassigned + s.Locations.Sum(x => x.Qty));
            Assert.True(s.Shortage <= 0);
            Assert.Equal(s.Locations.Select(x => x.Qty).OrderByDescending(q => q), s.Locations.Select(x => x.Qty));
        }
    }
}
