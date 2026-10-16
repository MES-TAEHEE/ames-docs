using System.Globalization;

namespace AMES.Data.Services.DemandPlan;

public sealed record DailyPlanItem(string PartNo, string PartName, string Unit, decimal PackQty, decimal[] Scheduled, decimal[] Po);

public sealed record DailyPlan(DateOnly From, DateOnly To, IReadOnlyList<DailyPlanItem> Items)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>
/// SRM MM30011 "MIP Purchase Plan Search (JIT)" 격자 → DailyPlan. 순수 함수(파일 읽기는 Web DailyPlanParser).
/// 열: 고정 12(NO·Vendor Code·Vendor Name·Car type·PART NO·PART NAME·Unit·Packing Qty.·D-1·Basic Inv·Current Inv·Open Order Qty)
///     + Sub total 2(Scheduled/P/O) + 날짜마다 2(Scheduled Qty. / P/O Qty). 2행은 라벨.
/// 날짜 앵커: 파일이 헤더를 MM/DD 로 쓰고 Excel 이 DD/MM 로 읽어 10/01~10/12 가 1월 10일…12월 10일로 저장되는 결함이 실측됐다.
/// 그래서 일(day) 13 이상인 첫 헤더만 믿고 anchor = 그 날짜 − k 로 두어 열 순서대로 하루씩 매긴다. 앵커가 기준일 ±60일 밖이면 다른 파일이다.
/// </summary>
public static class DailyPlanGrid
{
    public const int FixedCols = 12;
    public const int FirstDateCol = 14;   // 12 고정 + Sub total 2
    public const int MaxPartNoLen = 20;
    const int MaxAnchorDriftDays = 60;

    public static DailyPlan Parse(IReadOnlyList<string[]> rows, DateOnly today)
    {
        var body = rows.Where(r => r.Any(c => c.Length > 0)).ToList();
        if (body.Count < 3 || body[0].Length < FirstDateCol + 2
            || !Eq(body[0][4], "PART NO") || !body[0][7].StartsWith("Packing", StringComparison.OrdinalIgnoreCase)
            || !body[1][FirstDateCol].StartsWith("Scheduled", StringComparison.OrdinalIgnoreCase)
            || !body[1][FirstDateCol + 1].StartsWith("P/O", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("MM30011 일별 구매계획 테이블 형식이 아닙니다.");

        var headers = new List<string>();
        for (int c = FirstDateCol; c + 1 < body[0].Length && body[0][c].Length > 0; c += 2) headers.Add(body[0][c]);
        if (headers.Count == 0) throw new FormatException("MM30011 일별 구매계획 테이블 형식이 아닙니다.");

        var from = Anchor(headers, today);
        if (Math.Abs(from.DayNumber - today.DayNumber) > MaxAnchorDriftDays)
            throw new FormatException($"날짜 헤더가 기준일과 {MaxAnchorDriftDays}일 넘게 떨어져 있습니다 ({from:yyyy-MM-dd}).");
        var to = from.AddDays(headers.Count - 1);

        int width = FirstDateCol + headers.Count * 2;
        var items = new List<DailyPlanItem>();
        foreach (var raw in body.Skip(2))
        {
            var r = FitWidth(raw, width);
            if (r.Length < width) throw new FormatException($"품번 '{(r.Length > 4 ? r[4] : "?")}' 행의 셀 수가 {width}개가 아닙니다 ({r.Length}개).");
            var part = r[4].Trim();
            if (part.Length == 0) continue;
            if (part.Length > MaxPartNoLen) throw new FormatException($"품번 '{part}' 은 {MaxPartNoLen}자를 넘습니다.");
            var sched = new decimal[headers.Count];
            var po    = new decimal[headers.Count];
            for (int k = 0; k < headers.Count; k++)
            {
                sched[k] = Qty(r[FirstDateCol + 2 * k]);
                po[k]    = Qty(r[FirstDateCol + 2 * k + 1]);
            }
            items.Add(new DailyPlanItem(part, r[5].Trim(), r[6].Trim(), Qty(r[7]), sched, po));
        }
        return new DailyPlan(from, to, items);
    }

    static DateOnly Anchor(List<string> headers, DateOnly today)
    {
        for (int k = 0; k < headers.Count; k++)
            if (TryHeaderDate(headers[k], today, out var d) && d.Day >= 13) return d.AddDays(-k);

        // 전부 애매(일<13)한 짧은 파일 — header[0] 을 믿기 전에 파싱되는 다른 헤더가 하루씩 이어지는지 검사한다.
        // 안 이어지면 DD/MM 결함이 헤더마다 다르게 걸렸을 수 있어 header[0] 만으로 앵커를 정할 수 없다.
        if (!TryHeaderDate(headers[0], today, out var first))
            throw new FormatException("MM30011 일별 구매계획 테이블 형식이 아닙니다.");
        for (int k = 1; k < headers.Count; k++)
        {
            if (!TryHeaderDate(headers[k], today, out var d)) continue;
            if (d != first.AddDays(k))
                throw new FormatException($"날짜 헤더가 서로 이어지지 않습니다 ({headers[0]} 기준 {k}번째가 {headers[k]}).");
        }
        return first;
    }

    /// <summary>Excel 직렬값(1899-12-30 기준) · M/d/yyyy · MM/dd/yyyy · yyyy-MM-dd · M/d(기준일 연도, 6개월 넘게 과거면 다음 해).</summary>
    public static bool TryHeaderDate(string s, DateOnly today, out DateOnly d)
    {
        d = default;
        s = (s ?? "").Trim();
        if (s.Length == 0) return false;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial > 20000 && serial < 80000)
        {
            d = DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(Math.Floor(serial)));
            return true;
        }
        foreach (var fmt in new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" })
            if (DateOnly.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return true;
        if (DateTime.TryParseExact(s, "M/d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var md))
        {
            d = new DateOnly(today.Year, md.Month, md.Day);
            if (d.DayNumber < today.DayNumber - 180) d = d.AddYears(1);
            return true;
        }
        return false;
    }

    static bool Eq(string a, string b) => string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);

    static decimal Qty(string s)
        => s.Length == 0 ? 0m : decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);

    // Excel 재저장 시 꼬리 빈 셀이 생략/추가될 수 있어 폭을 보정한다(WeeklyPlanParser 와 같은 규칙).
    static string[] FitWidth(string[] r, int width)
    {
        if (r.Length > width) return r[..width];
        if (r.Length < width) { var p = new string[width]; Array.Fill(p, ""); r.CopyTo(p, 0); return p; }
        return r;
    }
}
