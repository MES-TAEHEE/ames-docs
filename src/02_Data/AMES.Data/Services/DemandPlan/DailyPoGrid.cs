using System.Globalization;
using AMES.Data.Services.Srm;

namespace AMES.Data.Services.DemandPlan;

public sealed record DailyPoItem(string PartNo, string PartName, string Unit, decimal? PackQty, string VendorCode, decimal[] Order, decimal[] Delivered);

public sealed record DailyPoPlan(string? VendorCode, DateOnly From, DateOnly To, IReadOnlyList<DailyPoItem> Items)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>
/// SRM MM31007 "발주정보 조회 (납기일자별)" → 품번 × 납기일 발주량·납품량.
/// 양식: 고정 열(업체·PART NO·품명·저장위치·발주단위·적입량 …) 다음에 날짜 머리글(MM-dd, 연도 없음)이 날짜마다 두 열(발주량·납품량)을 덮는다.
/// 열은 머리글 이름으로 찾고, 날짜는 머리글 칸(병합으로 두 열에 복사된 값)에서 읽는다. 순수 함수 — 정본 테스트 DailyPoGridTests.
/// </summary>
public static class DailyPoGrid
{
    static readonly string[] PartNoNames = { "PART NO" };
    static readonly string[] PartNmNames = { "품명", "PART NAME" };
    static readonly string[] UnitNames   = { "발주단위", "Unit" };
    static readonly string[] PackNames   = { "적입량", "Packing Qty." };
    static readonly string[] VendorNames = { "업체", "Vendor Code" };
    const string OrderLabel = "발주량";
    const string DelivLabel = "납품량";

    public static DailyPoPlan Parse(IReadOnlyList<string[]> rows, DateOnly today)
    {
        int h = -1;
        for (int i = 0; i < rows.Count; i++)
            if (SrmGridFind(rows[i], PartNoNames) >= 0) { h = i; break; }
        if (h < 0 || h + 1 >= rows.Count)
            throw new FormatException("MM31007 일별 발주 테이블(PART NO 머리글)을 찾을 수 없습니다.");

        var head = rows[h];
        var label = rows[h + 1];
        int part = SrmGridFind(head, PartNoNames), partNm = SrmGridFind(head, PartNmNames), unit = SrmGridFind(head, UnitNames),
            pack = SrmGridFind(head, PackNames), vendor = SrmGridFind(head, VendorNames);

        var missing = new List<string>();
        if (part < 0) missing.Add("PART NO");
        if (unit < 0) missing.Add("발주단위");
        if (pack < 0) missing.Add("적입량");
        if (missing.Count > 0) throw new FormatException("MM31007 필수 열이 없습니다: " + string.Join(", ", missing));

        // 날짜 쌍: 라벨 행에서 발주량 열 j, 바로 옆 j+1 이 납품량. 날짜는 머리글 j 칸.
        var pairs = new List<(int Order, int Deliv, DateOnly Date)>();
        for (int j = 0; j < label.Length; j++)
        {
            if (!string.Equals(label[j].Trim(), OrderLabel, StringComparison.OrdinalIgnoreCase)) continue;
            if (j + 1 >= label.Length || !string.Equals(label[j + 1].Trim(), DelivLabel, StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"MM31007 {j + 1}번째 열의 발주량 바로 다음에 납품량 열이 없습니다.");
            if (!TryMonthDay(head[j], today, out var date))
                throw new FormatException($"MM31007 날짜 머리글 '{head[j]}' 을 읽을 수 없습니다(MM-dd 형식).");
            pairs.Add((j, j + 1, date));
        }
        if (pairs.Count == 0) throw new FormatException("MM31007 날짜별 발주량·납품량 열을 찾을 수 없습니다.");

        var from = pairs.Min(p => p.Date);
        var to = pairs.Max(p => p.Date);
        var dates = pairs.OrderBy(p => p.Date).ToList();
        int days = to.DayNumber - from.DayNumber + 1;

        var items = new List<DailyPoItem>();
        string? vendorCode = null;
        for (int i = h + 2; i < rows.Count; i++)
        {
            var r = rows[i];
            var partNo = Cell(r, part);
            if (partNo.Length == 0) continue;
            if (partNo.Length > 20) throw new FormatException($"MM31007 품번 '{partNo}' 은 20자를 넘습니다.");

            var order = new decimal[days];
            var deliv = new decimal[days];
            foreach (var p in pairs)
            {
                int k = p.Date.DayNumber - from.DayNumber;
                order[k] += Qty(Cell(r, p.Order), OrderLabel, i);
                deliv[k] += Qty(Cell(r, p.Deliv), DelivLabel, i);
            }
            var v = Cell(r, vendor);
            vendorCode ??= v.Length > 0 ? v : null;
            items.Add(new DailyPoItem(partNo, Cell(r, partNm), Cell(r, unit),
                ParsePack(Cell(r, pack)), v, order, deliv));
        }

        if (items.Count == 0) throw new FormatException("MM31007 일별 발주 테이블에 품번 행이 없습니다.");
        return new DailyPoPlan(vendorCode, from, to, items);
    }

    /// <summary>"MM-dd"(연도 없음) → 기준일 연도. 기준일보다 6개월 넘게 과거면 다음 해(주간·일별 헤더 규칙과 같음). 다른 형식은 DailyPlanGrid 규칙.</summary>
    public static bool TryMonthDay(string s, DateOnly today, out DateOnly d)
    {
        d = default;
        s = (s ?? "").Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{1,2}-\d{1,2}$")
            && DateTime.TryParseExact(s, new[] { "M-d", "MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var md))
        {
            d = new DateOnly(today.Year, md.Month, md.Day);
            if (d.DayNumber < today.DayNumber - 180) d = d.AddYears(1);
            return true;
        }
        return DailyPlanGrid.TryHeaderDate(s, today, out d);
    }

    static string Cell(string[] r, int c) => c >= 0 && c < r.Length ? r[c].Trim() : "";

    static decimal? ParsePack(string s) => s.Length == 0 ? null
        : decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    static decimal Qty(string s, string col, int row)
    {
        if (s.Length == 0) return 0m;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var q)) return q;
        throw new FormatException($"MM31007 {row + 1}행 '{col}' 값 '{s}' 을 숫자로 읽을 수 없습니다.");
    }

    static int SrmGridFind(string[] head, string[] names) => PoListGrid.Find(head, names);
}
