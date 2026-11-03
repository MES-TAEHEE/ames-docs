using AMES.Data.Services;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    internal static readonly string RemainingProductionDemandSql = $"""
        SELECT w.WoID, w.ItemNo, ISNULL(w.OrderQty,0)-ISNULL(w.CompletedQty,0) AS Qty, w.DueDate
        FROM dbo.PP_WorkOrder w
        WHERE ISNULL(w.Status,'Draft') NOT IN ({WoStatusRules.FinishedSqlList})
          AND w.ItemNo IS NOT NULL AND ISNULL(w.OrderQty,0)>ISNULL(w.CompletedQty,0);
        """;

    // Read current demand with the existing MRP rules; do not run MRP or save a snapshot.
    public MrpCalculator.Result GetRemainingMaterialRequirements()
    {
        var today = DbClock.Today;
        var demands = Query(RemainingProductionDemandSql, r => new MrpCalculator.Demand(
            GetInt(r, "WoID"), GetString(r, "ItemNo")!, GetDecimal(r, "Qty"), r["DueDate"] as DateTime?));
        var bom = Query(PpRepository.EffectiveBomSql, r => new MrpCalculator.BomLine(
            GetString(r, "ParentItemNo")!, GetString(r, "CompItemNo")!,
            GetDecimal(r, "QtyPer"), GetDecimal(r, "ScrapPct")), ("@Today", null, today));
        return MrpCalculator.Explode(demands, bom, new Dictionary<string, MrpCalculator.Supply>(), today);
    }
}
