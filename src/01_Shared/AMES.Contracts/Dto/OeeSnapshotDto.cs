namespace AMES.Contracts.Dto;

/// <summary>
/// PP_LineOEE 한 행 (라인 × 일자 × 교대 스냅샷).
/// DB 는 비율을 소수(0.9208)로 저장하지만 DTO 는 백분율(92.08)로 든다 — 화면·목표값 비교가 모두 % 기준.
/// </summary>
public class OeeSnapshotDto
{
    public int      OeeSnapshotId { get; set; }
    public string   LineId        { get; set; } = "";
    public DateTime PeriodDate    { get; set; }
    public string?  ShiftCode     { get; set; }

    // ── 시간 (분) ────────────────────────────────────────────────────────
    public int LoadingMin       { get; set; }
    public int PlannedDownMin   { get; set; }
    public int UnplannedDownMin { get; set; }
    public int OperatingMin     { get; set; }

    // ── 수량 ────────────────────────────────────────────────────────────
    public decimal TotalProducedQty { get; set; }
    public decimal GoodQty          { get; set; }

    // ── 비율 (0–100 %) ──────────────────────────────────────────────────
    public decimal Availability { get; set; }
    public decimal Performance  { get; set; }
    public decimal Quality      { get; set; }
    public decimal Oee          { get; set; }

    public DateTime? CreatedTs { get; set; }
    public string?   CreatedBy { get; set; }

    /// <summary>계산 시 BOP 표준 사이클이 없어 P 를 100% 로 간주한 결과</summary>
    public bool PerformanceAssumed { get; set; }

    public decimal DefectQty => Math.Max(0, TotalProducedQty - GoodQty);

    // ── 시간 손실 모델 (분) — OperatingMin 을 P·Q 로 쪼갠다 ──────────────
    public int OeeMin         => (int)Math.Round(OperatingMin * (double)Performance / 100.0 * (double)Quality / 100.0);
    public int PerfLossMin    => (int)Math.Round(OperatingMin * (1.0 - (double)Performance / 100.0));
    public int QualityLossMin => Math.Max(0, OperatingMin - PerfLossMin - OeeMin);
    public int TotalLossMin   => PlannedDownMin + UnplannedDownMin + PerfLossMin + QualityLossMin;
}
