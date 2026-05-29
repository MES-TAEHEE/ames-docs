namespace AMES.Contracts.Dto;

/// <summary>
/// Current bond-machine recipe loaded against a line / WO.
/// Powers IMG-06 (Bond Setup) display + IMG-03 cycle confirm.
/// </summary>
public sealed class BondSetupDto
{
    public required int      BondSetupId { get; init; }
    public required string   LineId      { get; init; }
    public int?              WoId        { get; init; }
    public string?           RecipeId    { get; init; }
    public required decimal  PressureSp  { get; init; }   // bar
    public required decimal  TempSp      { get; init; }   // °C
    public required int      HoldSecSp   { get; init; }   // seconds
    public decimal?          TensionSp   { get; init; }   // N
    public DateTime?         LoadedAt    { get; init; }
    public string?           Status      { get; init; }   // APPLIED / PENDING
}
