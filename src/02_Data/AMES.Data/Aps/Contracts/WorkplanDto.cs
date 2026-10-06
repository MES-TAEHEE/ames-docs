namespace AMES.Data.Aps.Contracts;

/// 손으로 고친(잠긴) 칸만 저장하는 작업 계획. 프론트 saveWork/loadWork 와 같은 형태.
public sealed class Workplan
{
    public string LineCd { get; set; } = "";
    public string BaseDate { get; set; } = "";
    public List<WorkplanAsmCell> Assembly { get; set; } = new();
    public List<WorkplanInjCell> Injection { get; set; } = new();
    public DateTimeOffset? SavedAt { get; set; }
}
public sealed record WorkplanAsmCell(string PartNo, string Date, double Supply, bool Locked);
public sealed record WorkplanInjCell(string PartNo, string Date, double Day, double Night, bool Locked);
