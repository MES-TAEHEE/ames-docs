using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AMES.Web.Services;

/// <summary>
/// SRM ZMM30004 주간 구매계획 파서. 두 가지 실체를 자동 감지:
/// · .xls  — EUC-KR 인코딩 HTML 테이블
/// · .xlsx — 진짜 OOXML (날짜는 텍스트 셀)
/// 헤더 1행 = NO/PART NO/PART NAME/Unit/Base Inv. + 주 시작일(MM/dd/yyyy) N개,
/// 헤더 2행 = 주차 라벨([28/1W]) N개, 이후 데이터 행.
/// </summary>
public static class WeeklyPlanParser
{
    public sealed record Plan(IReadOnlyList<DateTime> WeekStarts,
                              IReadOnlyList<string> WeekLabels,
                              IReadOnlyList<Item> Items);

    public sealed record Item(string PartNo, string PartName, string Unit,
                              decimal BaseInv, decimal[] Qty);

    private const int FixedCols = 5;   // NO, PART NO, PART NAME, Unit, Base Inv.

    private static readonly Regex TrRx =
        new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TdRx =
        new(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Plan Parse(Stream stream)
    {
        var rows = (XlsxSheetReader.IsXlsx(stream)
                ? XlsxSheetReader.ReadRows(stream)
                : ReadHtmlRows(stream))
            .Where(r => r.Any(c => c.Length > 0))
            .ToList();

        if (rows.Count < 3 || rows[0].Length < FixedCols + 1)
            throw new FormatException("ZMM30004 주간 구매계획 테이블 형식이 아닙니다.");

        var weekStarts = rows[0].Skip(FixedCols)
            .TakeWhile(t => t.Length > 0)
            .Select(t => DateTime.ParseExact(t, "MM/dd/yyyy", CultureInfo.InvariantCulture))
            .ToList();
        if (weekStarts.Count == 0)
            throw new FormatException("ZMM30004 주간 구매계획 테이블 형식이 아닙니다.");

        // HTML은 라벨 행에 라벨만, xlsx는 고정 열 자리의 빈 셀 뒤에 라벨이 온다.
        var labelRow = rows[1].Length >= FixedCols + weekStarts.Count
            ? rows[1].Skip(FixedCols).ToArray() : rows[1];
        if (labelRow.Length < weekStarts.Count)
            throw new FormatException("주차 라벨 행이 주 시작일 개수와 맞지 않습니다.");
        var weekLabels = labelRow.Take(weekStarts.Count).ToList();

        int width = FixedCols + weekStarts.Count;
        var items = new List<Item>(rows.Count - 2);
        foreach (var raw in rows.Skip(2))
        {
            var r = FitWidth(raw, width);
            if (r.Length != width)
                throw new FormatException(
                    $"품번 '{(r.Length > 1 ? r[1] : "?")}' 행의 셀 수가 {width}개가 아닙니다 ({r.Length}개).");
            if (r[1].Length == 0) continue;   // 품번 없는 행(합계 등) 방어

            items.Add(new Item(r[1], r[2], r[3],
                ParseQty(r[4]),
                r.Skip(FixedCols).Select(ParseQty).ToArray()));
        }
        return new Plan(weekStarts, weekLabels, items);
    }

    private static List<string[]> ReadHtmlRows(Stream stream)
    {
        string html;
        using (var rd = new StreamReader(stream, Encoding.GetEncoding(51949)))
            html = rd.ReadToEnd();

        return TrRx.Matches(html)
            .Select(m => TdRx.Matches(m.Groups[1].Value)
                .Select(c => WebUtility.HtmlDecode(c.Groups[1].Value).Trim())
                .ToArray())
            .ToList();
    }

    // Excel 재저장 시 꼬리 빈 셀이 생략/추가될 수 있어 폭을 보정.
    // 잘리는 쪽에 내용이 있으면 그대로 두어 호출부 검증에서 실패하게 한다.
    private static string[] FitWidth(string[] r, int width)
    {
        if (r.Length > width)
            return r.Skip(width).All(c => c.Length == 0) ? r[..width] : r;
        if (r.Length < width)
        {
            var p = new string[width];
            Array.Fill(p, "");
            r.CopyTo(p, 0);
            return p;
        }
        return r;
    }

    private static decimal ParseQty(string s) =>
        s.Length == 0 ? 0m : decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);
}
