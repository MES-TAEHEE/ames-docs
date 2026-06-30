namespace AMES.Contracts.Dto;

public class LineStateLogDto
{
    public int      LogId     { get; set; }
    public string   LineId    { get; set; } = "";
    public DateTime LogDate   { get; set; }
    public string   StateCode { get; set; } = "LOAD"; // LOAD / PLANNED_DOWN / UNPLANNED_DOWN / BREAK
    public int      StartMin  { get; set; }
    public int      EndMin    { get; set; }
    public int?     ActualOutput      { get; set; }
    public int?     GoodOutput        { get; set; }
    public int?     TheoreticalOutput { get; set; }
    public string?  Notes     { get; set; }
    public DateTime CreatedAt { get; set; }
    public string?  CreatedBy { get; set; }

    public int    DurationMin => Math.Max(0, EndMin - StartMin);
    public string StartTime   => $"{StartMin / 60:00}:{StartMin % 60:00}";
    public string EndTime     => $"{EndMin   / 60:00}:{EndMin   % 60:00}";
}
