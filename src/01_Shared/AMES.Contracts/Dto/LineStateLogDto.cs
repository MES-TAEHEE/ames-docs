namespace AMES.Contracts.Dto;

/// <summary>PP_LineStateLog 한 행 — 라인 상태 분단위 로그(ODM). 읽기 전용.</summary>
public class LineStateLogDto
{
    public long     StateLogId { get; set; }
    public string   LineId     { get; set; } = "";
    public DateTime MinuteTs   { get; set; }
    public string?  State      { get; set; }   // RUN / IDLE / DOWN
    public string?  PlanState  { get; set; }   // PLAN-RUN / PLAN-DOWN
    public bool     RunFlag    { get; set; }
    public int?     WoId       { get; set; }
}
