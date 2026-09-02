namespace AMES.Contracts.Enums;

/// <summary>
/// MD_Item.ItemType enumeration.
/// Matches the CK constraint values used in dbo.MD_Item.
/// </summary>
public enum ItemType
{
    RAW,        // Raw material
    FABRIC,     // Fabric / vinyl
    POWDER,     // Powder coating
    PAINT,      // Liquid paint
    SUB,        // Sub-part (semi-finished / WIP)
    ASSY,       // Assembly (finished good)
    MATERIAL    // Purchased material (e.g. wiring harness)
}
