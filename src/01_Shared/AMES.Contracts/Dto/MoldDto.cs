namespace AMES.Contracts.Dto;

/// <summary>
/// MD_Mold row + computed lifecycle indicators for INJ-04/06.
/// </summary>
public sealed class MoldDto
{
    public required string MoldId         { get; init; }
    public required string MoldName       { get; init; }
    public int             RatedShots     { get; init; }
    public int             CurrentShots   { get; init; }
    public int             CavityCount    { get; init; }
    public DateTime?       LastMaintDate  { get; init; }
    public string?         Status         { get; init; }   // Available / Mounted / Maintenance / Retired

    public double LifePct =>
        RatedShots == 0 ? 0 : (double)CurrentShots * 100 / RatedShots;

    /// <summary>info / warn / alert / hard — see VOL05_INJ06 §02.</summary>
    public string Severity =>
        LifePct switch
        {
            >= 110 => "hard",
            >= 100 => "alert",
            >=  90 => "warn",
            >=  80 => "info",
            _      => "ok",
        };
}
