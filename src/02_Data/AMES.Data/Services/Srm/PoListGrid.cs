using System.Globalization;

namespace AMES.Data.Services.Srm;

/// <summary>
/// SRM MM31006 구매오더(P/O) 리스트 → 수주 행. <b>열 머리글 이름으로 열을 찾는다</b>(고정 위치 아님 — 열 순서·개수가 바뀌어도 읽는다).
/// 머리글 행은 "PART NO" 와 "발주번호"(또는 P/O No.)가 같이 있는 행. 필수 열이 없으면 빠진 이름을 모아 FormatException 으로 알린다.
/// 데이터 행은 발주번호 칸에 숫자가 있는 행만 읽는다(머리글 아래 보조 표·합계 행 방어).
/// </summary>
public static class PoListGrid
{
    public sealed record PoRow(string SoNumber, int? SoLineNo, string VendorCode, string VendorName,
        string ItemNo, string PartName, string Unit, decimal OrderQty, decimal ShippedQty,
        DateTime? OrderDate, DateTime? RequestedDeliveryDate);

    static readonly string[] PoNoNames     = { "발주번호", "P/O No." };
    static readonly string[] PartNoNames   = { "PART NO" };
    static readonly string[] DueNames      = { "납기일자", "ArrReq. Date" };
    static readonly string[] OrderQtyNames = { "발주량", "P/O Qty" };
    static readonly string[] VendorNames   = { "업체", "Vendor Code" };
    static readonly string[] VendorNmNames = { "업체명", "Vendor Name" };
    static readonly string[] OrderDtNames  = { "발주일자", "Order Date" };
    static readonly string[] PartNmNames   = { "품명", "PART NAME" };
    static readonly string[] UnitNames     = { "발주단위", "Unit" };
    static readonly string[] DelivNames    = { "납품수량", "Deliv. Qty" };

    public static IReadOnlyList<PoRow> Parse(IReadOnlyList<string[]> rows)
    {
        int h = -1;
        for (int i = 0; i < rows.Count; i++)
            if (Find(rows[i], PoNoNames) >= 0 && Find(rows[i], PartNoNames) >= 0) { h = i; break; }
        if (h < 0)
            throw new FormatException("MM31006 구매오더 리스트 머리글(발주번호·PART NO)을 찾을 수 없습니다.");

        var head = rows[h];
        int poNo = Find(head, PoNoNames), part = Find(head, PartNoNames), due = Find(head, DueNames), ordQ = Find(head, OrderQtyNames);
        var missing = new List<string>();
        if (poNo < 0) missing.Add(PoNoNames[0]);
        if (part < 0) missing.Add(PartNoNames[0]);
        if (due < 0) missing.Add(DueNames[0]);
        if (ordQ < 0) missing.Add(OrderQtyNames[0]);
        if (missing.Count > 0)
            throw new FormatException("MM31006 필수 열이 없습니다: " + string.Join(", ", missing));

        int vendor = Find(head, VendorNames), vendorNm = Find(head, VendorNmNames), orderDt = Find(head, OrderDtNames),
            partNm = Find(head, PartNmNames), unit = Find(head, UnitNames), deliv = Find(head, DelivNames);

        var result = new List<PoRow>();
        for (int i = h + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var (soNo, line) = SplitPoNo(Cell(r, poNo));
            if (soNo.Length == 0 || !soNo.Any(char.IsDigit)) continue;   // 합계·보조 표 행

            result.Add(new PoRow(soNo, line,
                Cell(r, vendor), Cell(r, vendorNm),
                Cell(r, part), Cell(r, partNm), Cell(r, unit),
                Qty(Cell(r, ordQ), "발주량", i), Qty(Cell(r, deliv), "납품수량", i),
                ParseDate(Cell(r, orderDt)), ParseDate(Cell(r, due))));
        }

        if (result.Count == 0)
            throw new FormatException("MM31006 구매오더 리스트에 유효한 P/O 행이 없습니다.");
        return result;
    }

    /// <summary>머리글 행에서 이름(후보 순)의 열 번호. 없으면 -1. 공백·대소문자 무시.</summary>
    public static int Find(string[] head, string[] names)
    {
        for (int c = 0; c < head.Length; c++)
            foreach (var n in names)
                if (string.Equals(head[c].Trim(), n, StringComparison.OrdinalIgnoreCase)) return c;
        return -1;
    }

    static string Cell(string[] r, int c) => c >= 0 && c < r.Length ? r[c].Trim() : "";

    // "4100162200-20" → ("4100162200", 20). 접미사 없으면 라인 null.
    public static (string SoNumber, int? SoLineNo) SplitPoNo(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return ("", null);
        int dash = s.LastIndexOf('-');
        if (dash <= 0 || dash == s.Length - 1) return (s, null);
        var head = s[..dash];
        var tail = s[(dash + 1)..];
        return int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? (head, n) : (s, null);
    }

    static DateTime? ParseDate(string s)
    {
        if (s.Length == 0 || s.StartsWith("0000")) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    static decimal Qty(string s, string col, int row)
    {
        if (s.Length == 0) return 0m;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var q)) return q;
        throw new FormatException($"MM31006 {row + 1}행 '{col}' 값 '{s}' 을 숫자로 읽을 수 없습니다.");
    }
}
