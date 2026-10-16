using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

public static class SparePartValidation
{
    public static List<PdaSparePartWorkOrder> OrderWorkOrders(IEnumerable<PdaSparePartWorkOrder> orders, string? equipment)
        => orders.OrderByDescending(w => !string.IsNullOrWhiteSpace(equipment)
                && ((!string.IsNullOrWhiteSpace(w.EquipId) && equipment.Contains(w.EquipId, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(w.EquipName) && equipment.Contains(w.EquipName, StringComparison.OrdinalIgnoreCase))))
            .ToList();

    public static bool FallsBelowSafetyStock(decimal currentQty, decimal releaseQty, int? safetyStock)
        => safetyStock is > 0 && currentQty - releaseQty < safetyStock.Value;

    public static string[] MissingFields(params (string Name, string? Value)[] fields) => fields
        .Where(f => string.IsNullOrWhiteSpace(f.Value) || f.Value.Trim() == "-")
        .Select(f => f.Name).ToArray();
}
