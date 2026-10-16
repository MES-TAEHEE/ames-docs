namespace AMES.Data.Aps.Domain;

public static class StockRoll
{
    /// 아침재고(toDate) = 실사(baselineDate 아침) + Σ_{baselineDate ≤ d < toDate} (생산 − 소비 + defect).
    /// 사출품의 소비는 부모 완제품 I0 × usage, 완제품의 소비는 출고(O1+O4). 실사가 없으면 굴리지 않고 0 · counted=false.
    public static (double qty, int rolledDays, bool counted) Roll(double? baselineQty,
        IEnumerable<(string date, double produced, double consumed, double defect)> daily, string baselineDate, string toDate)
    {
        var days0 = ApsCalendar.Parse(toDate).DayNumber - ApsCalendar.Parse(baselineDate).DayNumber;
        if (!baselineQty.HasValue) return (0, days0, false);   // 원본: 실사에 없으면 실적을 굴리지 않고 0
        var qty = baselineQty.Value;
        foreach (var d in daily)
            if (string.CompareOrdinal(d.date, baselineDate) >= 0 && string.CompareOrdinal(d.date, toDate) < 0)
                qty += d.produced - d.consumed + d.defect;
        var days = ApsCalendar.Parse(toDate).DayNumber - ApsCalendar.Parse(baselineDate).DayNumber;
        return (qty, days, baselineQty.HasValue);
    }
}
