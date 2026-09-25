using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>Quantity display follows the unit master; stored quantities are never rounded here.</summary>
public sealed class PortalQuantityFormat(MasterDataRepository masterData)
{
    Dictionary<string, int> _precision = new(StringComparer.OrdinalIgnoreCase);
    DateTime _expires;

    public string Format(string? unit)
    {
        if (DateTime.UtcNow >= _expires)
        {
            _precision = masterData.ListUoms().ToDictionary(x => x.UOMCode,
                x => Math.Clamp(x.DecimalPrec ?? 3, 0, 10), StringComparer.OrdinalIgnoreCase);
            _expires = DateTime.UtcNow.AddMinutes(1);
        }
        return $"N{(_precision.TryGetValue(unit?.Trim() ?? "", out var places) ? places : 3)}";
    }

    public string Text(decimal quantity, string? unit) => quantity.ToString(Format(unit));
}
