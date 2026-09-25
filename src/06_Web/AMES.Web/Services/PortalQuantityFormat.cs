namespace AMES.Web.Services;

/// <summary>Portal display shares the application UOM cache; numeric editors use a fixed format.</summary>
public sealed class PortalQuantityFormat(UomFormat units)
{
    public string Format(string? unit) => $"N{units.Digits(unit) ?? 3}";
    public string Text(decimal quantity, string? unit) => units.Qty(quantity, unit);
}
