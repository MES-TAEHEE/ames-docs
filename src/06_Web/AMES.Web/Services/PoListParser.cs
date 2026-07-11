using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AMES.Web.Services;

/// <summary>
/// SRM MM31006 구매오더(P/O) 리스트 파서. 두 가지 실체를 자동 감지:
/// · .xls  — EUC-KR 인코딩 HTML 테이블
/// · .xlsx — 진짜 OOXML (날짜는 텍스트 셀)
/// 헤더가 2행이고 데이터 행은 모두 35셀이므로 셀 수가 정확히 35개이면서
/// P/O No. 셀에 숫자가 있는 행만 데이터로 취급한다. 열은 고정 인덱스로 매핑.
/// </summary>
public static class PoListParser
{
    public sealed record PoRow(string SoNumber, int? SoLineNo, string VendorCode, string VendorName,
        string ItemNo, string PartName, string Unit, decimal OrderQty, decimal ShippedQty,
        DateTime? OrderDate, DateTime? RequestedDeliveryDate);

    private const int DataCols = 35;

    // 고정 열 인덱스 (0-based)
    private const int IdxVendorCode = 1;
    private const int IdxVendorName = 2;
    private const int IdxOrderDate  = 3;
    private const int IdxPoNo       = 4;
    private const int IdxArrReqDate = 5;
    private const int IdxPartNo     = 8;
    private const int IdxPartName   = 9;
    private const int IdxUnit       = 11;
    private const int IdxOrderQty   = 13;
    private const int IdxDelivQty   = 14;

    private static readonly Regex TrRx =
        new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TdRx =
        new(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<PoRow> Parse(Stream stream)
    {
        var dataRows = (XlsxSheetReader.IsXlsx(stream)
                ? XlsxSheetReader.ReadRows(stream)
                : ReadHtmlRows(stream))
            .Select(TrimEmptyTail)
            .Where(r => r.Length == DataCols)
            .ToList();

        if (dataRows.Count == 0)
            throw new FormatException("MM31006 구매오더 리스트 테이블 형식이 아닙니다.");

        var rows = new List<PoRow>(dataRows.Count);
        foreach (var r in dataRows)
        {
            var (soNo, line) = SplitPoNo(r[IdxPoNo]);
            // P/O No. 없는 행(합계 등)과 숫자 없는 행(xlsx 헤더 등) 방어
            if (soNo.Length == 0 || !soNo.Any(char.IsDigit)) continue;

            rows.Add(new PoRow(
                soNo, line,
                r[IdxVendorCode], r[IdxVendorName],
                r[IdxPartNo], r[IdxPartName], r[IdxUnit],
                ParseQty(r[IdxOrderQty]), ParseQty(r[IdxDelivQty]),
                ParseDate(r[IdxOrderDate]), ParseDate(r[IdxArrReqDate])));
        }

        if (rows.Count == 0)
            throw new FormatException("MM31006 구매오더 리스트에 유효한 P/O 행이 없습니다.");

        return rows;
    }

    private static List<string[]> ReadHtmlRows(Stream stream)
    {
        string html;
        using (var rd = new StreamReader(stream, Encoding.GetEncoding(51949)))
            html = rd.ReadToEnd();

        return TrRx.Matches(html)
            .Select(m => TdRx.Matches(m.Groups[1].Value)
                .Select(c => WebUtility.HtmlDecode(Strip(c.Groups[1].Value)).Trim())
                .ToArray())
            .ToList();
    }

    // xlsx 균일 폭 패딩 등으로 35셀보다 길어진 행의 빈 꼬리 셀 제거
    private static string[] TrimEmptyTail(string[] r)
    {
        if (r.Length <= DataCols) return r;
        return r.Skip(DataCols).All(c => c.Length == 0) ? r[..DataCols] : r;
    }

    // "4100162200-20" → ("4100162200", 20). 접미사 없으면 라인 null.
    private static (string SoNumber, int? SoLineNo) SplitPoNo(string s)
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

    private static DateTime? ParseDate(string s)
    {
        s = s.Trim();
        if (s.Length == 0 || s.StartsWith("0000")) return null;
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
    }

    private static decimal ParseQty(string s)
    {
        s = s.Trim();
        return s.Length == 0 ? 0m
            : decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var q) ? q : 0m;
    }

    private static string Strip(string cell) => Regex.Replace(cell, "<.*?>", string.Empty);
}
