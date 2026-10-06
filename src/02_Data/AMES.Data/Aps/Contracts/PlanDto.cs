namespace AMES.Data.Aps.Contracts;

/// /api/plan 응답 — 필드명·중첩은 docs/reference/aps-new-ui/api/plan_LQ10.json 그대로.
public sealed class PlanBundle
{
    public LineInfo Line { get; set; } = new();
    public string BaseDate { get; set; } = "";
    public string PrevDate { get; set; } = "";
    public List<string> Dates { get; set; } = new();
    public List<AssemblyRow> Assembly { get; set; } = new();
    public List<InjectionRow> Injection { get; set; } = new();
    public List<BomEdge> Bom { get; set; } = new();
    public string Source { get; set; } = "upload";
    public List<string> Warnings { get; set; } = new();
}

public sealed class AssemblyRow
{
    public string? Alc { get; set; }
    public string? Pgn { get; set; }
    public string? Model { get; set; }
    public string PartNo { get; set; } = "";
    public string? PartName { get; set; }
    public string? LineCd { get; set; }
    public double Uph { get; set; }
    public double OpeningStock { get; set; }
    public bool StockCounted { get; set; } = true;
    public double Defect { get; set; }
    public double SisProduced { get; set; }
    public double Shipped { get; set; }
    public double SafetyStock { get; set; }
    public List<AssemblyDay> Days { get; set; } = new();
}

public sealed class AssemblyDay
{
    public string Date { get; set; } = "";
    public double T { get; set; }
    public double Supply { get; set; }
    public double Demand { get; set; }
    public bool Locked { get; set; }
}

public sealed class InjectionRow
{
    public string? Group { get; set; }
    public string PartNo { get; set; } = "";
    public string? PartName { get; set; }
    public string? LineCd { get; set; }
    public double Uph { get; set; }
    public double OpeningStock { get; set; }
    public bool StockCounted { get; set; } = true;
    public double Defect { get; set; }
    public double SisProduced { get; set; }
    public double Used { get; set; }
    public double SafetyStock { get; set; }
    public string? MoldCode { get; set; }
    /// <summary>MD_MoldItem.Color — 같은 금형이라도 색상이 다르면 따로 찍으므로 형제(동시 취출)의 단위는 금형 × 색상이다. 원본에는 없다(null = 금형만).</summary>
    public string? MoldColor { get; set; }
    public int? Cavity { get; set; }   // 사출 능력 규칙이 없으면 null(생략) — 원본 plan-all 응답
    public int PackSize { get; set; }
    public List<string> SiblingPartNos { get; set; } = new();
    public List<InjectionDay> Days { get; set; } = new();

    /// <summary>동시 취출 그룹 키 — 금형/색상, 색상 없으면 금형, 금형 없으면 품번(원본 규칙). 스케줄러·부하 계산이 같은 키로 묶는다.</summary>
    public string MoldGroupKey() => MoldCode is null ? PartNo : MoldColor is null ? MoldCode : MoldCode + "/" + MoldColor;
}

public sealed class InjectionDay
{
    public string Date { get; set; } = "";
    public double Requirement { get; set; }
    public double PlanDay { get; set; }
    public double PlanNight { get; set; }
    public bool Locked { get; set; }
}

public sealed record BomEdge(string ParentPartNo, string ChildPartNo, double QtyPer);

public sealed class PlanResult
{
    public List<AssemblyResult> Assembly { get; set; } = new();
    public List<InjectionResult> Injection { get; set; } = new();
    public List<SummaryRow> Summary { get; set; } = new();
}

public sealed class AssemblyResult { public string PartNo { get; set; } = ""; public List<AssemblyCell> Cells { get; set; } = new(); }

public sealed class AssemblyCell
{
    public string Date { get; set; } = "";
    public double Stock { get; set; }
    public double T { get; set; }
    public double Supply { get; set; }
    public double Demand { get; set; }
    public string Status { get; set; } = "ok";
}

public sealed class InjectionResult { public string PartNo { get; set; } = ""; public List<InjectionCell> Cells { get; set; } = new(); }

public sealed class InjectionCell
{
    public string Date { get; set; } = "";
    public double Stock { get; set; }
    public double Requirement { get; set; }
    public double PlanDay { get; set; }
    public double PlanNight { get; set; }
    public double Remain { get; set; }
    public string Status { get; set; } = "ok";
    public bool OverRack { get; set; }
    public double? Shots { get; set; }
    public double? RunHours { get; set; }
}

public sealed class SummaryRow
{
    public string Date { get; set; } = "";
    public double AssemblyDemand { get; set; }
    public double AssemblySupply { get; set; }
    public double InjectionRequirement { get; set; }
    public double InjectionPlan { get; set; }
    public double RunHours { get; set; }
    public int ShortageCount { get; set; }
}

public sealed class LoadRow
{
    public string LineCd { get; set; } = "";
    public string Date { get; set; } = "";
    public double Hours { get; set; }
    public double DayHours { get; set; }
    public double NightHours { get; set; }
    public double Capacity { get; set; }
    public double Rate { get; set; }
    public bool Over { get; set; }
}

public sealed class Trace
{
    public string PartNo { get; set; } = "";
    public string Date { get; set; } = "";
    public double OpeningStock { get; set; }
    public double Requirement { get; set; }
    public double Shortfall { get; set; }
    public double Planned { get; set; }
    public double PlanDay { get; set; }
    public double PlanNight { get; set; }
    public double Closing { get; set; }
    public int PackSize { get; set; }
    public double Uph { get; set; }
    public double RunHours { get; set; }
    public string? MoldGroup { get; set; }
    public List<string> Siblings { get; set; } = new();
    public List<TraceStep> Steps { get; set; } = new();
}

public sealed class TraceStep
{
    public string Label { get; set; } = "";
    public string Calc { get; set; } = "";
    public string? Screen { get; set; }
    public bool Warn { get; set; }
}

public sealed class PlanFilters
{
    public List<string> Models { get; set; } = new();
    public List<string> Pgns { get; set; } = new();
    public string? Model { get; set; }
    public string? Pgn { get; set; }
}

public sealed class UploadRef { public string Id { get; set; } = ""; public string? StdDate { get; set; } public int Files { get; set; } }

public sealed class PlanResponse
{
    public PlanBundle Bundle { get; set; } = new();
    public PlanResult Result { get; set; } = new();
    public List<LoadRow> Loads { get; set; } = new();
    public List<Trace> Traces { get; set; } = new();
    public PlanFilters Filters { get; set; } = new();
    public UploadRef Upload { get; set; } = new();
}

public sealed class CalcResponse
{
    public PlanBundle Bundle { get; set; } = new();
    public PlanResult Result { get; set; } = new();
    public List<LoadRow> Loads { get; set; } = new();
    public List<Trace> Traces { get; set; } = new();
    public List<string> ScheduleWarnings { get; set; } = new();
}

/// 공급 자동 계산 근거 — 완제품 × 날짜 (autofill_LQ10.json 의 traces).
public sealed class AutofillTrace
{
    public string PartNo { get; set; } = "";
    public string Date { get; set; } = "";
    public double PrevClosing { get; set; }
    public double Demand { get; set; }
    public double DaysOfCover { get; set; }
    public double TargetStock { get; set; }
    public double BaseDemand { get; set; }
    public int PackSize { get; set; }
    public double RawNeed { get; set; }
    public double Supply { get; set; }
    public double Closing { get; set; }
}

public sealed class AutofillResponse
{
    public PlanBundle Bundle { get; set; } = new();
    public PlanResult Result { get; set; } = new();
    public Settings Settings { get; set; } = new();
    public List<AutofillTrace> Traces { get; set; } = new();
    public List<LoadRow> Loads { get; set; } = new();
    public List<Trace> ScheduleTraces { get; set; } = new();
    public List<string> ScheduleWarnings { get; set; } = new();
}

public sealed record AutofillBody(PlanBundle Bundle);
