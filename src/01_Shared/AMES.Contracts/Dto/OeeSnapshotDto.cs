namespace AMES.Contracts.Dto;

public class OeeSnapshotDto
{
    public int      OeeId       { get; set; }
    public string   LineId      { get; set; } = "";
    public string   PeriodType  { get; set; } = "DAY";   // DAY / WEEK / MONTH
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd   { get; set; }

    // ── Time buckets (minutes) ───────────────────────────────────────────
    public int LoadMin          { get; set; }
    public int PlannedDownMin   { get; set; }
    public int UnplannedDownMin { get; set; }
    public int AvailMin         { get; set; }

    // ── Rates (0–100 %) ─────────────────────────────────────────────────
    public decimal Availability { get; set; }
    public decimal Performance  { get; set; }
    public decimal Quality      { get; set; }
    public decimal OeeRate      { get; set; }
    public decimal TargetOee    { get; set; } = 75m;

    // ── Output counts ───────────────────────────────────────────────────
    public int ActualOutput       { get; set; }
    public int TheoreticalOutput  { get; set; }
    public int GoodOutput         { get; set; }

    public DateTime CreatedAt { get; set; }
    public string?  CreatedBy { get; set; }

    // ── Computed ─────────────────────────────────────────────────────────
    public bool IsAboveTarget => OeeRate >= TargetOee;
    public int  DefectCount   => Math.Max(0, ActualOutput - GoodOutput);

    public string PeriodLabel => PeriodType switch {
        "WEEK"  => $"W{System.Globalization.ISOWeek.GetWeekOfYear(PeriodStart):00}",
        "MONTH" => PeriodStart.ToString("yy-MM"),
        _       => PeriodStart.ToString("MM/dd")
    };

    // ── Time-loss model segments (minutes) ───────────────────────────────
    // OEE waterfall: LoadMin = PlannedDown + UnplannedDown + PerfLoss + QualLoss + OeeMin
    public int OeeMin {
        get {
            if (LoadMin <= 0) return 0;
            double av = AvailMin, p = (double)Performance / 100.0, q = (double)Quality / 100.0;
            return (int)Math.Round(av * p * q);
        }
    }
    public int QualityLossMin {
        get {
            if (LoadMin <= 0) return 0;
            double av = AvailMin, p = (double)Performance / 100.0, q = (double)Quality / 100.0;
            return (int)Math.Round(av * p * (1.0 - q));
        }
    }
    public int PerfLossMin {
        get {
            if (LoadMin <= 0) return 0;
            return (int)Math.Round(AvailMin * (1.0 - (double)Performance / 100.0));
        }
    }
}
