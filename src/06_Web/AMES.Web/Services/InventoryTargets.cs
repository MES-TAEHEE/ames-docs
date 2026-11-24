using AMES.Data.Repositories;

namespace AMES.Web.Services;

public static class InventoryTargets
{
    public static decimal Quantity(IEnumerable<RptRepository.InventoryTarget> targets,string category,string unit)
    {
        var rows=targets.Where(t=>t.Category==category&&t.Unit==unit).ToList();
        // New edits are shared by category/unit. Keep older WH/FG settings visible without a DB migration.
        return rows.FirstOrDefault(t=>t.Scope=="ALL")?.Qty??rows.Where(t=>t.Scope is "WH" or "FG").Sum(t=>t.Qty);
    }
}
