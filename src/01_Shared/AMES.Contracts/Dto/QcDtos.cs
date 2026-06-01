namespace AMES.Contracts.Dto;

/// <summary>One row in the QC_Inspection table (QC-01/02/03).</summary>
public sealed class QcInspectionDto
{
    public int      InspectionId    { get; init; }
    public string   InspectionNo    { get; init; } = "";
    public string   InspectionType  { get; init; } = "";    // Incoming / InProcess / Final
    public int?     LotId           { get; init; }
    public int?     WoId            { get; init; }
    public string?  LineId          { get; init; }
    public string?  ItemNo          { get; init; }
    public string?  ItemName        { get; init; }
    public string?  Mode            { get; init; }
    public int      SampleSize      { get; init; }
    public decimal  BatchQty        { get; init; }
    public int      CumulativeGood  { get; init; }
    public int      DefectQtyTotal  { get; init; }
    public string   Verdict         { get; init; } = "";    // PASS / FAIL / IN_PROGRESS
    public bool     CriticalFlag    { get; init; }
    public string?  InspectorId     { get; init; }
    public DateTime InsStartTs      { get; init; }
    public DateTime? InsEndTs       { get; init; }
}

/// <summary>One measurement row for an inspection.</summary>
public sealed class QcInspectionItemDto
{
    public int      InspectionItemId { get; init; }
    public int      InspectionId     { get; init; }
    public int      ItemSeq          { get; init; }
    public string   ItemName         { get; init; } = "";
    public string?  Standard         { get; init; }
    public string?  Measured         { get; init; }
    public string?  Result           { get; init; }        // PASS / FAIL
}

/// <summary>Inspection standard summary (QC-08).</summary>
public sealed class QcInspectionStdDto
{
    public int      StdId         { get; init; }
    public string   StdCode       { get; init; } = "";
    public string?  VerNo         { get; init; }
    public string?  StdName       { get; init; }
    public string?  InsType       { get; init; }
    public string?  ItemNo        { get; init; }
    public string?  ItemName      { get; init; }
    public decimal  AqlLevel      { get; init; }
    public int      SampleInterval{ get; init; }
    public string?  InspItemsJson { get; init; }
    public string?  Status        { get; init; }
    public DateTime? EffectiveDate{ get; init; }
}

/// <summary>NCR row (QC-04).</summary>
public sealed class QcNcrDto
{
    public int      NcrId        { get; init; }
    public string   NcrNumber    { get; init; } = "";
    public string?  SourceType   { get; init; }
    public string?  SourceId     { get; init; }
    public string?  Severity     { get; init; }     // Minor / Major / Critical
    public int?     LotId        { get; init; }
    public string?  ItemNo       { get; init; }
    public string?  ItemName     { get; init; }
    public decimal  AffectedQty  { get; init; }
    public string?  Disposition  { get; init; }     // HOLD / REWORK / SCRAP / RTV / USE-AS-IS
    public string?  Status       { get; init; }     // Open / Investigating / Closed
    public string?  ReportedBy   { get; init; }
    public DateTime? ReportedAt  { get; init; }
    public int?     HoldId       { get; init; }
    public int?     CapaId       { get; init; }
}

/// <summary>Hold row (QC-05).</summary>
public sealed class QcHoldDto
{
    public int      HoldId        { get; init; }
    public string   HoldNumber    { get; init; } = "";
    public int?     SourceNcrId   { get; init; }
    public string?  NcrNumber     { get; init; }
    public string?  Severity      { get; init; }
    public string?  AffectedType  { get; init; }    // LOT / WO / FG
    public int?     LotId         { get; init; }
    public string?  ItemNo        { get; init; }
    public string?  ItemName      { get; init; }
    public decimal  HeldQty       { get; init; }
    public string?  PhysicalLocation { get; init; }
    public string?  Status        { get; init; }    // Held / Released / Rejected
    public DateTime? HeldAt       { get; init; }
}

/// <summary>CAPA row (QC-06).</summary>
public sealed class QcCapaDto
{
    public int      CapaId       { get; init; }
    public string   CapaNumber   { get; init; } = "";
    public string?  Type         { get; init; }     // Corrective / Preventive
    public string?  TriggerType  { get; init; }     // NCR / Audit / Customer
    public string?  Phase        { get; init; }     // Plan / Action / Verify / Close
    public string?  Status       { get; init; }
    public string?  RootCause    { get; init; }
    public string?  Cause4M      { get; init; }
    public string?  OwnerId      { get; init; }
    public DateTime? OpenedAt    { get; init; }
    public DateTime? DueDate     { get; init; }
    public DateTime? ClosedAt    { get; init; }
    public int      ActionsTotal     { get; init; }
    public int      ActionsCompleted { get; init; }
}

/// <summary>CAPA action / step (QC-06 detail panel).</summary>
public sealed class QcCapaActionDto
{
    public int      CapaActionId { get; init; }
    public int      CapaId       { get; init; }
    public string?  ActionType   { get; init; }
    public int      CheckDay     { get; init; }
    public string?  Description  { get; init; }
    public string?  Metric       { get; init; }
    public string?  TargetValue  { get; init; }
    public string?  ActualValue  { get; init; }
    public string?  Verdict      { get; init; }
    public DateTime? DueDate     { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>Defect rate by module (QC-07 dashboard tile).</summary>
public sealed class QcModuleDefectDto
{
    public string  Module      { get; init; } = "";     // INJ / IMG / PNT
    public int     GoodQty     { get; init; }
    public int     DefectQty   { get; init; }
    public decimal DefectPct   => (GoodQty + DefectQty) == 0 ? 0 : DefectQty * 100m / (GoodQty + DefectQty);
}

/// <summary>Top-N defect pareto entry (QC-07).</summary>
public sealed class QcDefectParetoDto
{
    public string  DefectCode  { get; init; } = "";
    public string? DefectName  { get; init; }
    public int     Count       { get; init; }
}

/// <summary>Traceability node (QC-TRC).</summary>
public sealed class QcTraceNodeDto
{
    public string  Kind         { get; init; } = "";     // LOT / WO / MATERIAL / FG / SHIPMENT
    public string  RefId        { get; init; } = "";
    public string? Title        { get; init; }
    public string? Subtitle     { get; init; }
    public DateTime? OccurredAt { get; init; }
}
