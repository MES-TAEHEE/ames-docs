namespace AMES.Contracts.Dto;

/// <summary>Today's daily plan row (PNT-01).</summary>
public sealed class PntDailyPlanDto
{
    public int      PlanId       { get; init; }
    public DateTime PlanDate     { get; init; }
    public string   ItemNo       { get; init; } = "";
    public string?  ItemName     { get; init; }
    public string   RalColor     { get; init; } = "";
    public string?  ColorName    { get; init; }
    public string?  HexValue     { get; init; }
    public int      TargetQty    { get; init; }
    public string?  LineId       { get; init; }
    public string?  OvenId       { get; init; }
    public string?  StartTime    { get; init; }     // HH:mm
    public int      JigsRequired { get; init; }
    public int      LotsRequired { get; init; }
    public bool     ReadyFlag    { get; init; }
    public int      LotsIssued   { get; init; }     // computed: # of VirtualLots
    public int      LotsBound    { get; init; }     // computed: # BOUND
}

/// <summary>Virtual lot row (PNT-02 / PNT-04 / PNT-06).</summary>
public sealed class PntVirtualLotDto
{
    public int      VirtualLotId     { get; init; }
    public int?     LotId            { get; init; }
    public int      PlanId           { get; init; }
    public string?  JigId            { get; init; }
    public string   ItemNo           { get; init; } = "";
    public string?  ItemName         { get; init; }
    public string   RalColor         { get; init; } = "";
    public string?  ColorName        { get; init; }
    public string?  HexValue         { get; init; }
    public int      TargetQty        { get; init; }
    public int      LoadedQty        { get; init; }
    public int      ConfirmedQty     { get; init; }
    public int      DefectQty        { get; init; }
    public string   Status           { get; init; } = "PRE";
    public bool     EnhancedInspection { get; init; }
    public DateTime IssuedAt         { get; init; }
    public DateTime? BindAt          { get; init; }
    public string?  BindReason       { get; init; }
}

/// <summary>Jig with current binding (PNT-02 picker + PNT-03 loading).</summary>
public sealed class PntJigDto
{
    public string  JigId         { get; init; } = "";
    public string? JigName       { get; init; }
    public int     HangerCount   { get; init; }
    public int     RatedCycle    { get; init; }
    public int     CycleCount    { get; init; }
    public string? HealthStatus  { get; init; }
    public decimal ReadFailRate  { get; init; }
    public bool    Available     { get; init; }     // not currently bound to a lot
    public int     LifePct       => RatedCycle == 0 ? 0 : (int)Math.Clamp(CycleCount * 100.0 / RatedCycle, 0, 100);
}

/// <summary>RFID line event for the track board (PNT-04).</summary>
public sealed class PntLineEventDto
{
    public long     EventId    { get; init; }
    public string?  TagId      { get; init; }
    public string?  JigId      { get; init; }
    public int?     LotId      { get; init; }
    public string?  ReaderId   { get; init; }       // R1/R2/R3
    public string?  GateLocation { get; init; }
    public DateTime EventTs    { get; init; }
    public short?   Rssi       { get; init; }
    public string?  TriggerType { get; init; }
}

/// <summary>Single 5-second oven sample (PNT-05 chart).</summary>
public sealed class PntOvenSampleDto
{
    public string   OvenId   { get; init; } = "";
    public byte?    ZoneId   { get; init; }
    public decimal  TempC    { get; init; }
    public DateTime SampledAt{ get; init; }
}

/// <summary>Oven KPI rollup (PNT-05 top panel).</summary>
public sealed class PntOvenStatusDto
{
    public string  OvenId     { get; init; } = "";
    public string? OvenName   { get; init; }
    public int     TargetTemp { get; init; }
    public int     Tolerance  { get; init; }
    public int     DwellSec   { get; init; }
    public decimal CurrentTemp{ get; init; }
    public decimal MinTemp24h { get; init; }
    public decimal MaxTemp24h { get; init; }
    public int     JigsInside { get; init; }
}

/// <summary>Shift report bucket (PNT-09).</summary>
public sealed class PntShiftBucketDto
{
    public string  ShiftCode  { get; init; } = "";
    public int     LotsClosed { get; init; }
    public int     GoodQty    { get; init; }
    public int     DefectQty  { get; init; }
    public decimal DefectPct  => (GoodQty + DefectQty) == 0 ? 0 : DefectQty * 100m / (GoodQty + DefectQty);
}

/// <summary>RAL color (master).</summary>
public sealed class PntRalColorDto
{
    public string  RalCode   { get; init; } = "";
    public string? ColorName { get; init; }
    public string? HexValue  { get; init; }
    public int     CureTemp  { get; init; }
    public int     CureDuration { get; init; }
}
