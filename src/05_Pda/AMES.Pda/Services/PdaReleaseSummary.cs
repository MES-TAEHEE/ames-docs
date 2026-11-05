using System.Globalization;

namespace AMES.Pda.Services;

internal static class PdaReleaseSummary
{
    public static string Lot(string lotNo, string? partNo, decimal qty, string unit)
        => $"LOT: {lotNo}\nPart: {Text(partNo)}\nQty: {qty.ToString("#,##0.###", CultureInfo.InvariantCulture)} {unit}";

    public static string Picking(IEnumerable<PdaApi.ReleaseLotRow> lots, string reason, Func<string?, string> unitSymbol)
        => string.Join("\n\n", lots.Select(lot => Lot(lot.LotNo, lot.ItemNo, lot.Qty, unitSymbol(lot.Unit))))
            + $"\nReason: {Text(reason)}";

    public static string Direct(string lotNo, string? partNo, decimal qty, string unit, string reason)
        => Lot(lotNo, partNo, qty, unit) + $"\nReason: {Text(reason)}";

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
