namespace AMES.Contracts.Dto;

/// <summary>
/// Snapshot of a fabric / leather roll that's been issued (or available to be
/// issued) to a wrapping line. Sourced from tbl_Lot rows whose ProcessCode='WH'
/// or whose ItemNo refers to a FABRIC item, enriched with MD_PaintFabric color
/// metadata.
/// </summary>
public sealed class FabricRollDto
{
    public required int     LotId         { get; init; }
    public required string  LotCode       { get; init; }
    public required string  ItemNo        { get; init; }
    public string?          ItemName      { get; init; }
    public string?          ColorCode     { get; init; }    // GY-C03, BK-C01 …
    public required decimal RemainingM    { get; init; }    // metres left on roll
    public DateTime?        ExpiryDate    { get; init; }
    public string?          Status        { get; init; }    // OPEN / EXHAUSTED
}
