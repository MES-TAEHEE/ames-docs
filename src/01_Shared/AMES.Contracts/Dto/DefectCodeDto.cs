namespace AMES.Contracts.Dto;

/// <summary>
/// MD_DefectCode row used to render the 6-card defect picker on INJ-05.
/// </summary>
public sealed class DefectCodeDto
{
    public required string DefectCode    { get; init; }     // e.g. INJ-D02
    public required string DefectName    { get; init; }     // 미성형
    public string?         DefectNameEn  { get; init; }     // Short Shot
    public string?         ProcessCode   { get; init; }     // INJ
    public string?         SeverityLevel { get; init; }
    public string?         DefaultCauseCode { get; init; }
}
