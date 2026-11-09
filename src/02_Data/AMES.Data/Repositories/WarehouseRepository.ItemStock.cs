using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    /// <param name="OnHand">양수 재고 합 — 위치 무관(PP-APS·PP-003·MRP 재고와 같은 기준)</param>
    /// <param name="Unassigned">위치 미지정(팔레트에 실린 채 입고 전) 수량</param>
    /// <param name="Shortage">라인 자동 차감 자리표시(BF-) 음수 합 — 실물 LOT 이 아직 올라오지 않아 장부에서 모자란 양(0 이하)</param>
    /// <param name="Locations">위치별 양수 수량, 많은 순(위치 미지정 제외)</param>
    public sealed record ItemStockSummary(decimal OnHand, int Lots, decimal Unassigned, decimal Shortage,
        IReadOnlyList<(string Location, decimal Qty)> Locations);

    /// <summary>품번 하나의 현재 재고 요약(품번 BOM 미리보기 팝업용, 읽기 전용).</summary>
    public ItemStockSummary GetItemStockSummary(string partNo)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT LocationNo,
                   SUM(CASE WHEN Qty > 0 THEN Qty ELSE 0 END) AS Pos,
                   SUM(CASE WHEN Qty < 0 THEN Qty ELSE 0 END) AS Neg,
                   COUNT(CASE WHEN Qty > 0 THEN 1 END)        AS Lots
            FROM   dbo.WH_Inventory
            WHERE  PartNo = @P AND Qty <> 0
            GROUP  BY LocationNo
            """, conn);
        ColumnParams.AddFilter(cmd, "@P", "WH_Inventory.PartNo", partNo);
        decimal onHand = 0, unassigned = 0, shortage = 0;
        var lots = 0;
        var locs = new List<(string, decimal)>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var loc = r["LocationNo"] as string;
                var pos = Convert.ToDecimal(r["Pos"]);
                onHand   += pos;
                shortage += Convert.ToDecimal(r["Neg"]);
                lots     += Convert.ToInt32(r["Lots"]);
                if (pos <= 0) continue;
                if (string.IsNullOrWhiteSpace(loc)) unassigned += pos;
                else locs.Add((loc.Trim(), pos));
            }
        }
        return new ItemStockSummary(onHand, lots, unassigned, shortage,
            locs.OrderByDescending(x => x.Item2).ThenBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
