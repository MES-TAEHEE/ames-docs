using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

/// 프론트 aps.js 의 shiftFor · asmCapFor · packFor · roundUp 과 같은 규칙 (ANALYSIS.md §4.4 · §4.5).
public sealed class ShiftRules(Settings settings, IReadOnlyDictionary<string, LineInfo> lines)
{
    static string Key(string? v) => (v ?? "").Trim().ToUpperInvariant();

    public bool TrimLastDay => settings.TrimLastDay;

    /// 날짜 예외 → 라인별 근무 → (사출기만) 공장 기본. 조립 라인에 아무것도 없으면 src "없음".
    public (double day, double night, string src, string? note) ShiftFor(string lineCd, string date, bool injection)
    {
        var c = Key(lineCd);
        var ex = settings.ShiftExceptions.FirstOrDefault(x => x.Date == date && Key(x.LineCd) == c)
              ?? settings.ShiftExceptions.FirstOrDefault(x => x.Date == date && Key(x.LineCd) == "");
        if (ex != null) return (ex.Day, ex.Night, "예외", ex.Note ?? "");
        var ls = settings.LineShifts.FirstOrDefault(x => Key(x.LineCd) == c);
        if (ls != null) return (ls.Day, ls.Night, "라인", null);
        return injection ? (settings.Shift.Day, settings.Shift.Night, "사출 기본", null) : (0, 0, "없음", null);
    }

    /// 조립 라인 하루 능력 = floor(UPH × 줄 수 × 시간). 모르면 null (dailyCapacity > 0 이면 그 값).
    /// LineShift.DailyCap 이 있으면 그 값 — AMES 완제품 라인(MD_Line.DailyCap). 휴무일은 Dates 에 들어오지 않으므로 여기서 0 처리하지 않는다 (Notes #15).
    public (double cap, double uph, int stations, double hours)? AsmCapFor(string lineCd, string date)
    {
        var ls = settings.LineShifts.FirstOrDefault(x => Key(x.LineCd) == Key(lineCd));
        if (ls?.DailyCap is int dc) return (dc, 0, 1, 0);
        var master = lines.TryGetValue(lineCd, out var l) ? l.Uph : 0;
        var uph = ls?.Uph is > 0 ? ls.Uph.Value : master;
        var st = Math.Max(1, ls?.Stations ?? 1);
        var sh = ShiftFor(lineCd, date, false);
        if (sh.src == "없음" || uph <= 0)
            return settings.DailyCapacity > 0 ? (settings.DailyCapacity, 0, 1, 0) : null;
        var h = sh.day + sh.night;
        return (Math.Floor(uph * st * h), uph, st, h);
    }

    /// 포장 단위 — 가장 긴 앞자리 규칙, 없으면 roundTo.
    public int PackFor(string partNo)
    {
        var p = (partNo ?? "").ToUpperInvariant();
        PackRule? best = null;
        foreach (var r in settings.PackRules)
        {
            var pre = Clean(r.Prefix).ToUpperInvariant();
            if (pre.Length > 0 && p.StartsWith(pre) && (best == null || pre.Length > Clean(best.Prefix).Length)) best = r;
        }
        return best?.PackSize ?? settings.RoundTo;
    }

    /// 사출 능력 규칙 — 가장 긴 앞자리.
    public UphRule? UphRuleFor(string partNo)
    {
        var p = (partNo ?? "").ToUpperInvariant();
        return settings.UphRules
            .Where(r => r.CleanPrefix.Length > 0 && p.StartsWith(r.CleanPrefix.ToUpperInvariant()))
            .OrderByDescending(r => r.CleanPrefix.Length).FirstOrDefault();
    }

    /// 재고일수 — 일 평균 수요가 「이상」인 첫 구간(내림차순), 없으면 기본값.
    public double CoverFor(double avgDailyDemand)
    {
        foreach (var t in settings.CoverTiers.OrderByDescending(t => t.MinDailyDemand))
            if (avgDailyDemand >= t.MinDailyDemand) return t.DaysOfCover;
        return settings.DefaultDaysOfCover;
    }

    public static double RoundUp(double v, int pack) => v <= 0 ? 0 : pack > 1 ? Math.Ceiling(v / pack) * pack : Math.Ceiling(v);

    static string Clean(string? v) => (v ?? "").Replace("%", "").Replace("*", "").Replace(" ", "");
}
