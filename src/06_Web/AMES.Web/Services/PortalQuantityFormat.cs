namespace AMES.Web.Services;

/// <summary>Portal display shares the application UOM cache; numeric editors use a fixed format.</summary>
public sealed class PortalQuantityFormat(UomFormat units)
{
    public string Format(string? unit) => units.Digits(unit) is { } digits && digits <= 2 ? $"N{digits}" : "#,##0.00####";
    public string Text(decimal quantity, string? unit) => AMES.Contracts.Formatting.UomQty.Format(quantity, units.Digits(unit) is { } digits ? Math.Min(digits, 2) : null);
}
